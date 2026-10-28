using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using BatterySimulator.Interfaces;
using Domain;


namespace BatterySimulator
{
    public class BatterySimulator : IDisposable
    {
        private SimulationTimeProvider? _timeProvider;
        private bool _isRealTime;
        private BatteryDataRecorder? _recorder;
        private IBatteryEMS? _ems;
        private IBatteryPlanner? _planner;
        private IPriceForecaster? _priceForecaster;
        private PnLManager? _pnlManager;
        private DateTime _end;
        private DateTime _start;
        private bool _simulationEnabled;
        private EnergyMarket? _market;
        private Battery? _battery;
        private TimeSpan _sleepTime;
        private OrderBookExecutor? _executor;
        private BatteryWearModel? _wear;

        public BatterySimulator(IBatteryPlanner planner)
        {
            _planner = planner;
            
        }

        public bool IsRealTime
        {
            get => _isRealTime;
            set => _isRealTime = value;
        }

        public BatteryDataRecorder Recorder => _recorder;

        public bool SimulationEnabled
        {
            get => _simulationEnabled;
            set => _simulationEnabled = value;
        }

        public DateTime Start
        {
            get => _start;
            set => _start = value;
        }

        public TimeSpan Delta
        {
            get => TimeProvider.TimeStep;
            set => TimeProvider.TimeStep = value;
        }

        
        public DateTime SimulationTime
        {
            get { return TimeProvider.GetTime(); }
        }

        public SimulationTimeProvider TimeProvider
        {
            get => _timeProvider;
            set => _timeProvider = value;
        }

        public IPriceForecaster PriceForecaster
        {
            get => _priceForecaster;
            set => _priceForecaster = value;
        }

        public PnLManager? PnlManager
        {
            get => _pnlManager;
            set => _pnlManager = value;
        }

        public Battery Battery1
        {
            get => _battery;
            set => _battery = value;
        }

        public TimeSpan SleepTime
        {
            get => _sleepTime;
            set => _sleepTime = value;
        }

        public IBatteryPlanner Planner
        {
            get => _planner;
            set => _planner = value;
        }


        public SimulationSettings Settings { get; set; } = new SimulationSettings();

        /// <summary>The price areas (CSV columns) available in <paramref name="priceFile"/>.</summary>
        public static IReadOnlyList<string> ReadPriceAreas(string priceFile = SimulationSettings.DefaultPriceFile)
        {
            return SpotPriceReader.ReadFile(priceFile).Keys.ToList();
        }

        /// <summary>The first and last delivery times with a price in <paramref name="priceFile"/> (over all areas).</summary>
        public static (DateTime First, DateTime Last) ReadPriceRange(string priceFile)
        {
            var points = SpotPriceReader.ReadFile(priceFile).Values.SelectMany(ts => ts.TimePoints()).ToList();
            return (points.Min(), points.Max());
        }

        public void SetUp(int nHours, int deltaSeconds)
        {
            Settings.DurationHours = nHours;
            Settings.TimeStepSeconds = deltaSeconds;
            SetUp(Settings);
        }

        public void SetUp(SimulationSettings settings)
        {
            var errors = settings.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
            Settings = settings;

            _start = DateTime.SpecifyKind(settings.Start, DateTimeKind.Local);
            _end = _start + TimeSpan.FromHours(settings.DurationHours);

            var prices = SpotPriceReader.ReadFile(settings.PriceFile);
            if (!prices.TryGetValue(settings.PriceArea, out TimeSeries? areaPrices))
                throw new ArgumentException($"Price area '{settings.PriceArea}' not found. Available: {string.Join(", ", prices.Keys)}.");

            // Planners look beyond the simulation end (look-ahead, planning horizon, POMA's 24 h storage value),
            // so the price data must cover that too.
            var timePoints = areaPrices.TimePoints().Take(2).ToList();
            DateTime dataStart = timePoints[0];
            // The last price is valid for one resolution step (an hour or a quarter-hour).
            DateTime dataEnd = areaPrices.TimePoints().Last() + (timePoints[1] - timePoints[0]);
            double lookBeyondHours = Math.Max(24, Math.Max(settings.LookAheadHours, settings.PlanningHorizonHours));
            if (_start < dataStart || _end.AddHours(lookBeyondHours) > dataEnd)
                throw new ArgumentException($"Prices cover {dataStart:yyyy-MM-dd HH:mm} to {dataEnd:yyyy-MM-dd HH:mm}; " +
                                            $"start + duration + {lookBeyondHours:0.#} h look-ahead must fit inside.");

            TimeSpan delta = TimeSpan.FromSeconds(settings.TimeStepSeconds);
            TimeProvider = new SimulationTimeProvider(_start, delta);

            _recorder = new BatteryDataRecorder(_timeProvider);

            _market = new EnergyMarket(Guid.NewGuid(), "EPEX Intraday");

            _market.Ts.EnergySellPrice = areaPrices;
            _market.Ts.EnergySellPrice.IsBreakPointSeries = true;
            _market.Ts.EnergyBuyPrice = _market.Ts.EnergySellPrice + TimeSeries.CreateTimeSeries(_market.Ts.EnergySellPrice.TimePoints(), settings.BuySellSpread);


            // exact
            _priceForecaster = new PriceForecaster(_market.Ts.EnergyBuyPrice, a: 0, b: 0);
            _priceForecaster.UpdateForecast(_start, TimeSpan.FromHours(settings.DurationHours), TimeSpan.FromMinutes(15));

            _battery = new Battery
            {
                Id = Guid.NewGuid(),
                Name = "Battery1",
                NominalChargeCapacity = new Power(settings.PowerMW, Units.MegaWatt),
                NominalEnergyCapacity =  new Energy(settings.EnergyMWh, Units.MegaWattHour),
                InitialSoHc = new Percentage(100),
                InitialSoHe = new Percentage(100),
                ChargeEfficiency = new DimensionlessQuantity(settings.ChargeEfficiency),
                DischargeEfficiency = new DimensionlessQuantity(settings.DischargeEfficiency),
                MaxNumberOfEfcPerHour = 100,
                InitialSoC = new Percentage(settings.InitialSoCPercent),
                SoftSchedulePenaltyPrice = new UnitPrice(100,new PriceUnit(Currencies.Euro,Units.MegaWatt)),
                FinalStoragePrice = new UnitPrice(0,new PriceUnit(Currencies.Euro,Units.MegaWatt)),
                Ts = new Battery.BatteryTs()
            };

            // Initial capacities depend only on nominal capacity and state-of-health,
            // so compute them here for every planner (not just inside PomaPlanner.SetUp).
            _battery.CalculateInitialCapacityC();
            _battery.CalculateInitialCapacityE();


            PriceUnit priceUnit = new PriceUnit(Currencies.Euro, Units.MegaWatt);
            _battery.DischargePoints =
            [
                new ChargePoint(new Power(0, Units.MegaWatt), new UnitPrice(0, priceUnit)),
                new ChargePoint(new Power(settings.PowerMW, Units.MegaWatt), new UnitPrice(0, priceUnit))
            ];
            _battery.ChargePoints =
            [
                new ChargePoint(new Power(0, Units.MegaWatt), new UnitPrice(0, priceUnit)),
                new ChargePoint(new Power(settings.PowerMW, Units.MegaWatt), new UnitPrice(0, priceUnit))
            ];


            // Must match Battery.InitialSoC, which POMA plans from.
            BatteryState initialState = new BatteryState
            {
                EnergyContent = new Energy(settings.EnergyMWh * settings.InitialSoCPercent / 100.0, Units.MegaWattHour),
                Capacity = new Energy(settings.EnergyMWh, Units.MegaWattHour)
            };


            _ems = new BatteryEMS(Battery1, initialState, _start);

            _recorder.Subscribe(_ems);
            

            // A snapshot like the book's: wear parameters apply on Start/Restart.
            _wear = settings.CreateWearModel();

            _planner.SetUp(new PlannerContext(
                _battery,
                _market,
                _priceForecaster.PriceForecast,
                initialState.EnergyContent.CovertToUnit(Units.MegaWattHour).Value,
                settings.WearAwarePlanning ? _wear : null,
                _ems));
            ApplyPlannerSettings();

            _pnlManager = new PnLManager(_recorder);
            _pnlManager.SetUp(_start);

            // The book's mids start at the price file's (day-ahead) prices; BuySellSpread is not used with the order book.
            // The book gets a snapshot: its parameters apply on Start/Restart, not to a running simulation.
            var bookSettings = settings.Clone();
            _executor = settings.MarketModel == MarketModel.OrderBook
                ? new OrderBookExecutor(new SyntheticOrderBook(_market.Ts.EnergySellPrice, bookSettings), bookSettings, _end)
                : null;

            // Day-ahead forecasts as known at gate closure; like the book, a snapshot for the run.
            _runSettings = bookSettings;
            _forecast = null;
            if (settings.DayAheadMarket)
            {
                _forecast = settings.DayAheadPerfectForecast
                    ? _market.Ts.EnergySellPrice
                    : SpotPriceReader.ReadColumn(settings.ForecastFile, $"{settings.PriceArea} forecast");
                var forecastPoints = _forecast.TimePoints().ToList();
                if (forecastPoints.Count == 0 || forecastPoints[0] > OrderBookExecutor.ProductStart(_start) || forecastPoints[^1] < _end.AddMinutes(-SimulationSettings.ProductMinutes))
                    throw new ArgumentException($"The day-ahead forecast in {settings.ForecastFile} must cover the whole run.");
            }
            _knowledge = new PriceKnowledge(_market.Ts.EnergySellPrice, _forecast, _executor?.Book, bookSettings);
            _pendingDayAheadBid = null;

            _sleepTime = TimeSpan.FromMilliseconds(settings.SleepMilliseconds);

        }

        private SimulationSettings _runSettings = new();
        private TimeSeries? _forecast;
        private PriceKnowledge? _knowledge;
        private IReadOnlyDictionary<DateTime, double>? _pendingDayAheadBid;

        /// <summary>When the day-ahead auction for delivery day <paramref name="day"/> closes.</summary>
        private DateTime DayAheadGateClosure(DateTime day) => day.AddDays(-1).AddHours(_runSettings.DayAheadGateClosureHour);

        /// <summary>
        /// Plans the day-ahead bid for the products of delivery day <paramref name="day"/> that the run delivers,
        /// at trading time <paramref name="t"/>. Products before them (the rest of today) are fixed at their
        /// current positions; no further trading in them is assumed. With <paramref name="warmStart"/>, the auction
        /// closed before the run and the bid uses the forecast even though the result is already published.
        /// </summary>
        private async Task<IReadOnlyDictionary<DateTime, double>> PlanDayAheadBid(DateTime t, DateTime day, bool warmStart)
        {
            var none = new Dictionary<DateTime, double>();
            if (_planner is not IDayAheadBidder bidder || _executor == null || _knowledge == null || _forecast == null) return none;

            TimeSpan productLength = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes);
            DateTime from = day > _start ? day : OrderBookExecutor.ProductStart(_start);
            DateTime to = day.AddDays(1) < _end ? day.AddDays(1) : _end;
            if (from >= to) return none;

            double hours = productLength.TotalHours;
            double powerMW = _battery.NominalChargeCapacity.CovertToUnit(Units.MegaWatt).Value;
            var quotes = new List<ProductQuote>();
            for (DateTime product = OrderBookExecutor.ProductStart(t); product < to; product += productLength)
            {
                if (product < from)
                {
                    double expected = _knowledge.Expected(product, t);
                    quotes.Add(new ProductQuote(product, expected, expected, 0, 0, _executor.Position(product) / hours));
                }
                else
                {
                    double forecast = warmStart ? _forecast[product] : _knowledge.DayAheadView(product, t);
                    quotes.Add(new ProductQuote(product, forecast, forecast, powerMW, powerMW, 0));
                }
            }

            double meanForecast = quotes.Where(q => q.Product >= from).Average(q => q.SellPrice);
            return await bidder.PlanDayAhead(t, new MarketView(quotes, meanForecast));
        }

        public DateTime End => _end;

        /// <summary>The battery wear charged in the profit, or null without <see cref="SimulationSettings.BatteryWear"/>.</summary>
        public BatteryWearModel? Wear => _wear;

        /// <summary>
        /// Wear over a step of length <paramref name="step"/> from SoC <paramref name="socAtStart"/> (fraction) with
        /// the recorded net charge (charge positive); POMA's net charge has the opposite sign.
        /// </summary>
        private Wear StepWear(double socAtStart, TimeSpan step, double recordedNetChargeMW)
        {
            if (_wear == null) return default;
            double lossMWh = _wear.LossRate(socAtStart, -recordedNetChargeMW) * step.TotalHours;
            double envelopeCost = _wear.CostPerMWhLost * _wear.EnvelopeLossRate(socAtStart, -recordedNetChargeMW) * step.TotalHours;
            return new Wear(_wear.CostPerMWhLost * lossMWh, lossMWh, envelopeCost);
        }

        /// <summary>The price file's (sell) prices keyed by local delivery hour: realized prices in the single-price market, day-ahead prices where the order book's mids start.</summary>
        public TimeSeries SellPrices => _market.Ts.EnergySellPrice;

        /// <summary>Single-price buy prices: <see cref="SellPrices"/> plus the buy/sell spread.</summary>
        public TimeSeries BuyPrices => _market.Ts.EnergyBuyPrice;

        /// <summary>The order-book executor when <see cref="MarketModel.OrderBook"/> is selected, otherwise null.</summary>
        public OrderBookExecutor? Executor => _executor;

        /// <summary>Raised on the simulation thread after every step with positions, plan and execution totals.</summary>
        public event EventHandler<ExecutionSnapshot>? StepCompleted;

        private ExecutionSnapshot BuildSnapshot(DateTime from, DateTime to, DateTime planEnd)
        {
            TimeSpan productLength = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes);
            DateTime lastPlanned = planEnd < _end ? planEnd : _end;
            // Also cover products already held beyond the plan, e.g. tomorrow's day-ahead volumes from 13:00.
            DateTime last = lastPlanned;
            if (_executor != null && _executor.Positions.Count > 0)
            {
                DateTime lastHeld = _executor.Positions.Keys.Max() + productLength;
                if (lastHeld > last) last = lastHeld;
            }
            bool dayAhead = _runSettings.DayAheadMarket && _executor != null;

            var products = new List<ProductSnapshot>();
            for (DateTime product = OrderBookExecutor.ProductStart(to); product < last; product += productLength)
            {
                double planned = product < lastPlanned ? -_planner.GetPlannedProduction(product).CovertToUnit(Units.MegaWatt).Value : double.NaN;
                double position = _executor != null ? _executor.Position(product) / productLength.TotalHours : double.NaN;
                double fillPrice = _executor?.AverageFillPrice(product) ?? double.NaN;
                double dayAheadMW = dayAhead ? _executor!.DayAheadVolume(product) / productLength.TotalHours : double.NaN;
                products.Add(new ProductSnapshot(product, position, planned, fillPrice, dayAheadMW));
            }

            return new ExecutionSnapshot(
                from, to, _recorder.NetCharge[to], products,
                HasOrderBook: _executor != null,
                GrossTradedMWh: _executor != null ? _executor.GrossBoughtMWh + _executor.GrossSoldMWh : 0,
                TradeCount: _executor?.TradeCount ?? 0,
                ShortImbalanceMWh: _executor?.ShortImbalanceMWh ?? 0,
                LongImbalanceMWh: _executor?.LongImbalanceMWh ?? 0,
                ImbalanceNetCost: _executor?.ImbalanceNetCost ?? 0,
                Quotes: _executor != null ? Quotes(_executor, to) : Array.Empty<BookQuote>(),
                HasDayAhead: _runSettings.DayAheadMarket && _executor != null,
                DayAheadNetCash: _executor?.DayAheadNetCash ?? 0,
                IntradayNetCash: _executor?.IntradayNetCash ?? 0);
        }

        /// <summary>
        /// The number of plan periods from <paramref name="t"/>: the planning horizon, extended to the end of the last
        /// product with a position. A plan that stopped short of held positions (e.g. the day-ahead purchases of the
        /// evening) would trade as if the battery were free afterwards and could commit more energy than it can hold.
        /// </summary>
        private int PeriodsCoveringPositions(DateTime t, TimeSpan resolution, int horizonPeriods)
        {
            if (_executor == null) return horizonPeriods;
            DateTime first = OrderBookExecutor.ProductStart(t);
            DateTime lastEnd = _executor.Positions
                .Where(p => p.Key >= first && Math.Abs(p.Value) > 1e-9)
                .Select(p => p.Key + TimeSpan.FromMinutes(SimulationSettings.ProductMinutes))
                .DefaultIfEmpty(t)
                .Max();
            int needed = (int)Math.Ceiling((lastEnd - t) / resolution);
            return Math.Max(horizonPeriods, needed);
        }

        /// <summary>
        /// Gives an order-book-aware planner the market as it looks at <paramref name="t"/>: for every product of the
        /// coming plan, its existing position and, if still tradable, the price and depth for further trades. Each
        /// side is priced at the average of a read-only walk sized to one product at full battery power, so the
        /// planner does not assume it can trade the whole book at the best price.
        /// </summary>
        private void PrepareMarketView(DateTime t, DateTime planEnd)
        {
            if (_planner is not IOrderBookAwarePlanner aware) return;
            if (_executor == null || !Settings.BookAwarePlanning)
            {
                aware.SetMarketView(null);
                return;
            }

            TimeSpan productLength = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes);
            double hours = productLength.TotalHours;
            double fullPowerVolume = _battery.NominalChargeCapacity.CovertToUnit(Units.MegaWatt).Value * hours;
            DateTime firstTradable = _executor.FirstTradableProduct(t);
            SyntheticOrderBook book = _executor.Book;

            var quotes = new List<ProductQuote>();
            for (DateTime product = OrderBookExecutor.ProductStart(t); product < planEnd; product += productLength)
            {
                double positionMW = _executor.Position(product) / hours;
                double mid = _knowledge!.Expected(product, t);

                if (product < firstTradable || !book.HasOpened(product, t))
                {
                    // Past gate closure (or not open yet): only the existing position can be delivered.
                    quotes.Add(new ProductQuote(product, mid, mid, 0, 0, positionMW));
                    continue;
                }

                var ask = book.QuoteForVolume(product, t, fullPowerVolume, askSide: true);
                var bid = book.QuoteForVolume(product, t, fullPowerVolume, askSide: false);
                quotes.Add(new ProductQuote(
                    product,
                    double.IsNaN(ask.AveragePrice) ? mid : ask.AveragePrice,
                    double.IsNaN(bid.AveragePrice) ? mid : bid.AveragePrice,
                    ask.DepthMWh / hours,
                    bid.DepthMWh / hours,
                    positionMW));
            }

            // What the market currently expects energy to be worth over the next day (beyond the plan): book mids, the
            // day-ahead price, or its forecast before the day-ahead result is published.
            double midSum = 0;
            int midCount = 0;
            for (DateTime product = OrderBookExecutor.ProductStart(t); product < t.AddHours(24); product += productLength)
            {
                midSum += _knowledge!.Expected(product, t);
                midCount++;
            }

            aware.SetMarketView(new MarketView(quotes, midSum / midCount));
        }

        /// <summary>
        /// The intraday curve as the market looks at trading time <paramref name="t"/>: every product of the current
        /// delivery day (from the run start), plus the next day's once their trading has opened, limited to the price
        /// data the run checked. Bid and ask are NaN for products that cannot be traded (past gate closure or not open
        /// yet); a delivered product's mid is its realized price.
        /// </summary>
        private List<BookQuote> Quotes(OrderBookExecutor executor, DateTime t)
        {
            TimeSpan productLength = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes);
            SyntheticOrderBook book = executor.Book;
            DateTime today = t.Date, tomorrow = t.Date.AddDays(1);
            DateTime first = OrderBookExecutor.ProductStart(_start > today ? _start : today);
            DateTime last = book.HasOpened(tomorrow, t) ? tomorrow.AddDays(1) : tomorrow;
            if (last > _end.AddHours(24)) last = _end.AddHours(24);
            DateTime firstTradable = executor.FirstTradableProduct(t);

            var quotes = new List<BookQuote>();
            for (DateTime product = first; product < last; product += productLength)
            {
                bool tradable = product >= firstTradable && book.HasOpened(product, t);
                var bids = tradable ? book.Bids(product, t) : Array.Empty<BookLevel>();
                var asks = tradable ? book.Asks(product, t) : Array.Empty<BookLevel>();
                quotes.Add(new BookQuote(
                    product,
                    bids.Count > 0 ? bids[0].Price : double.NaN,
                    asks.Count > 0 ? asks[0].Price : double.NaN,
                    executor.Book.Mid(product, t),
                    executor.Book.RealizedPrice(product)));
            }
            return quotes;
        }

        /// <summary>
        /// Pushes the planner-related <see cref="Settings"/> into the planner. Called in SetUp and before
        /// every re-plan, so these settings can be changed while a simulation runs. Invalid settings
        /// (e.g. half-edited in the GUI) are ignored and the planner keeps its previous values.
        /// </summary>
        private void ApplyPlannerSettings()
        {
            if (Settings.Validate().Count > 0) return;

            _planner.ApplySettings(new PlannerSettings(
                TimeSpan.FromHours(Settings.LookAheadHours),
                Settings.BuyThresholdPercent,
                Settings.SellThresholdPercent,
                Settings.SocSoftMinPercent,
                Settings.SocSoftMaxPercent,
                Settings.PomaRequestDirectory));
        }

        // Completed while running; a pending source while paused. The loop awaits it before every step.
        private TaskCompletionSource _running = CompletedSource();
        private readonly object _pauseLock = new();

        private static TaskCompletionSource CompletedSource()
        {
            var source = new TaskCompletionSource();
            source.SetResult();
            return source;
        }

        public bool IsPaused => !_running.Task.IsCompleted;

        /// <summary>Holds the simulation before its next step until <see cref="Resume"/>. Cancelling still stops a paused run.</summary>
        public void Pause()
        {
            lock (_pauseLock)
            {
                if (_running.Task.IsCompleted) _running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Resume()
        {
            lock (_pauseLock) _running.TrySetResult();
        }

        public async Task Simulate(CancellationToken cancellationToken = default)
        {
            Resume();   // a new run starts running, whatever the previous one was left in
            var priceUnit = new PriceUnit(Currencies.Euro, Units.MegaWattHour);
            DateTime t = TimeProvider.GetTime();
            TimeSpan replanningInterval = TimeSpan.FromMinutes(Settings.ReplanIntervalMinutes);
            TimeSpan planResolution = TimeSpan.FromMinutes(15);
            int planPeriods = (int)Math.Round(TimeSpan.FromHours(Settings.PlanningHorizonHours) / planResolution);

            // Day-ahead cash not yet booked; it goes into the next step's cash flows.
            (double BuyCost, double SellIncome) dayAhead = default;
            bool dayAheadMarket = _runSettings.DayAheadMarket && _executor != null;
            if (dayAheadMarket)
            {
                // Days whose auction closed before the run were bid then, from the forecast known at the time, and
                // start with their accepted volumes.
                for (DateTime day = _start.Date; day < _end && DayAheadGateClosure(day) < _start; day = day.AddDays(1))
                {
                    var bid = await PlanDayAheadBid(_start, day, warmStart: true);
                    var cash = _executor!.SettleDayAhead(bid, product => _market.Ts.EnergySellPrice[product]);
                    dayAhead = (dayAhead.BuyCost + cash.BuyCost, dayAhead.SellIncome + cash.SellIncome);
                }
            }

            int periods = PeriodsCoveringPositions(_start, planResolution, planPeriods);
            PrepareMarketView(_start, _start + periods * planResolution);
            await _planner.UpdatePlan(_start, planResolution, periods);

            DateTime lastPlanning = t;
            DateTime planEnd = t + periods * planResolution;

            try
            {
                while(t < _end && SimulationEnabled && !cancellationToken.IsCancellationRequested)
                {
                    if (IsPaused) await _running.Task.WaitAsync(cancellationToken);

                    // Horizon and re-plan interval may be changed while running; ignore invalid edits.
                    if (Settings.Validate().Count == 0)
                    {
                        replanningInterval = TimeSpan.FromMinutes(Settings.ReplanIntervalMinutes);
                        planPeriods = (int)Math.Round(TimeSpan.FromHours(Settings.PlanningHorizonHours) / planResolution);
                    }

                    if (dayAheadMarket)
                    {
                        // Bid tomorrow at gate closure; at publication the accepted volumes become positions.
                        DateTime tomorrow = t.Date.AddDays(1);
                        if (t == DayAheadGateClosure(tomorrow) && tomorrow < _end)
                            _pendingDayAheadBid = await PlanDayAheadBid(t, tomorrow, warmStart: false);
                        if (_pendingDayAheadBid != null && t == t.Date.AddHours(_runSettings.DayAheadPublicationHour))
                        {
                            var cash = _executor!.SettleDayAhead(_pendingDayAheadBid, product => _market.Ts.EnergySellPrice[product]);
                            dayAhead = (dayAhead.BuyCost + cash.BuyCost, dayAhead.SellIncome + cash.SellIncome);
                            _pendingDayAheadBid = null;
                        }
                    }

                    if (t - lastPlanning >= replanningInterval)
                    {
                        Console.Out.WriteLine($"Replanning...");

                        ApplyPlannerSettings();
                        periods = PeriodsCoveringPositions(t, planResolution, planPeriods);
                        PrepareMarketView(t, t + periods * planResolution);
                        await _planner.UpdatePlan(t, planResolution, periods);
                        planEnd = t + periods * planResolution;
                        lastPlanning = t;
                    }

                    // Implement plan/policy for the interval [t, t + Delta)
                    (double BuyCost, double SellIncome) trades = default;
                    if (_executor != null)
                    {
                        // Trade towards the plan; the battery delivers what has been traded.
                        trades = _executor.Trade(t, _planner, planEnd);
                        _ems.SetChargeLevel(_executor.DeliverySetpoint(t));
                    }
                    else
                    {
                        _ems.SetChargeLevel(-_planner.GetPlannedProduction(t));
                    }

                    // Non-blocking pacing; honours a Stop request promptly.
                    await Task.Delay(_sleepTime, cancellationToken);

                    if (_isRealTime)
                    {
                        await Task.Delay(Delta, cancellationToken);
                    }

                    // Advance the clock first, so the state update integrates the setpoint
                    // over [t, t + Delta] and observers record it at the interval end.
                    TimeProvider.Increment();
                    DateTime tNext = TimeProvider.GetTime();

                    // Wear depends on the SoC at the start of the step, as in POMA.
                    double socAtStart = _ems.GetSoC().ToFraction();
                    await _ems.UpdateState(tNext);
                    Wear wear = StepWear(socAtStart, tNext - t, _recorder.NetCharge[tNext]);

                    await Console.Out.WriteLineAsync($"{t}-{tNext:HH:mm}: Net charge = {_ems.GetChargeLevel()} SoC = {_ems.GetSoC().ToFraction():P1}");

                    if (_executor != null)
                    {
                        // Cash from this step's fills and day-ahead settlement, plus imbalance for what was delivered over [t, tNext).
                        var imbalance = _executor.SettleImbalance(t, tNext, _recorder.NetCharge[tNext], _executor.Book.RealizedPrice(OrderBookExecutor.ProductStart(t)));
                        _pnlManager.RecordCashFlows(tNext,
                            trades.BuyCost + dayAhead.BuyCost + imbalance.BuyCost,
                            trades.SellIncome + dayAhead.SellIncome + imbalance.SellIncome, wear);
                        dayAhead = default;
                    }
                    else
                    {
                        // Energy traded over [t, tNext) is settled at the prices valid at t.
                        _pnlManager.UpdatePnL(tNext, new UnitPrice(_market.Ts.EnergyBuyPrice[t],priceUnit),new UnitPrice(_market.Ts.EnergySellPrice[t],priceUnit), wear);
                    }

                    StepCompleted?.Invoke(this, BuildSnapshot(t, tNext, planEnd));

                    t = tNext;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stop requested — exit the simulation loop cleanly. Any other cancellation, such as an HTTP
                // timeout in a planner, is a failure and must not end the run as if it had completed.
            }
        }

        public void Dispose()
        {
            _ems?.EndTransmission();
        }
    }
}
