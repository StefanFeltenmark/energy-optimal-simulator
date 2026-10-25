using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using BatterySimulator.Interfaces;
using BatteryPomaPlanner;
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
            DateTime dataStart = areaPrices.TimePoints().First();
            DateTime dataEnd = areaPrices.TimePoints().Last().AddHours(1);
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
            

            // Generate a plan
            if (_planner is PriceLevelPlanner priceLevelPlanner)
            {
                priceLevelPlanner.EnergyPriceForecast = _priceForecaster.PriceForecast;
                priceLevelPlanner.Battery1 = _battery;
            }
            ApplyPlannerSettings();
            if(_planner is PomaPlanner pomaPlanner)
            {
                pomaPlanner.SetUp(_battery, _market);
                pomaPlanner.Subscribe(_ems);
            }
            if (_planner is RandomBatteryPlanner randomBatteryPlanner)
            {
                randomBatteryPlanner.Battery1 = _battery;
            }
            if (_planner is RetroPlanner retroPlanner)
            {
                retroPlanner.HistoricPrices = _market.Ts.EnergyBuyPrice;
                retroPlanner.PriceForecast = _priceForecaster.PriceForecast;
                retroPlanner.Battery1 = _battery;
                retroPlanner.LookBack = TimeSpan.FromHours(24);
            }

            _pnlManager = new PnLManager(_recorder);
            _pnlManager.SetUp(_start);

            // The book's mid converges to the realized (sell) price; BuySellSpread is not used with the order book.
            // The book gets a snapshot: its parameters apply on Start/Restart, not to a running simulation.
            var bookSettings = settings.Clone();
            _executor = settings.MarketModel == MarketModel.OrderBook
                ? new OrderBookExecutor(new SyntheticOrderBook(_market.Ts.EnergySellPrice, bookSettings), bookSettings, _end)
                : null;

            _sleepTime = TimeSpan.FromMilliseconds(settings.SleepMilliseconds);

        }

        public DateTime End => _end;

        /// <summary>Realized (sell) prices keyed by local delivery hour; the order book's mids converge to these.</summary>
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
            DateTime last = planEnd < _end ? planEnd : _end;

            var products = new List<ProductSnapshot>();
            for (DateTime product = OrderBookExecutor.ProductStart(to); product < last; product += productLength)
            {
                double planned = -_planner.GetPlannedProduction(product).CovertToUnit(Units.MegaWatt).Value;
                double position = _executor != null ? _executor.Position(product) / productLength.TotalHours : double.NaN;
                double fillPrice = _executor?.AverageFillPrice(product) ?? double.NaN;
                products.Add(new ProductSnapshot(product, position, planned, fillPrice));
            }

            return new ExecutionSnapshot(
                from, to, _recorder.NetCharge[to], products,
                HasOrderBook: _executor != null,
                GrossTradedMWh: _executor != null ? _executor.GrossBoughtMWh + _executor.GrossSoldMWh : 0,
                TradeCount: _executor?.TradeCount ?? 0,
                ShortImbalanceMWh: _executor?.ShortImbalanceMWh ?? 0,
                LongImbalanceMWh: _executor?.LongImbalanceMWh ?? 0,
                ImbalanceNetCost: _executor?.ImbalanceNetCost ?? 0,
                Quotes: _executor != null ? Quotes(_executor, to) : Array.Empty<BookQuote>());
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
                double mid = book.Mid(product, t);

                if (product < firstTradable)
                {
                    // Past gate closure: only the existing position can be delivered.
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

            // What the market currently expects energy to be worth over the next day (beyond the plan).
            double midSum = 0;
            int midCount = 0;
            for (DateTime product = OrderBookExecutor.ProductStart(t); product < t.AddHours(24); product += productLength)
            {
                midSum += book.Mid(product, t);
                midCount++;
            }

            aware.SetMarketView(new MarketView(quotes, midSum / midCount));
        }

        /// <summary>How far ahead (from the trading time) <see cref="ExecutionSnapshot.Quotes"/> covers open products.</summary>
        public const double QuoteHorizonHours = 12;

        /// <summary>Top of the book for all open products, as the market looks at trading time <paramref name="t"/>.</summary>
        private List<BookQuote> Quotes(OrderBookExecutor executor, DateTime t)
        {
            TimeSpan productLength = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes);
            DateTime last = t.AddHours(QuoteHorizonHours);

            var quotes = new List<BookQuote>();
            for (DateTime product = executor.FirstTradableProduct(t); product < last; product += productLength)
            {
                var bids = executor.Book.Bids(product, t);
                var asks = executor.Book.Asks(product, t);
                quotes.Add(new BookQuote(
                    product,
                    bids.Count > 0 ? bids[0].Price : double.NaN,
                    asks.Count > 0 ? asks[0].Price : double.NaN,
                    executor.Book.Mid(product, t),
                    _market.Ts.EnergySellPrice[product]));
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

            if (_planner is PriceLevelPlanner priceLevelPlanner)
            {
                priceLevelPlanner.LookaAhead = TimeSpan.FromHours(Settings.LookAheadHours);
                priceLevelPlanner.BuyPriceThreshold = new Percentage(Settings.BuyThresholdPercent);
                priceLevelPlanner.SellPriceThreshold = new Percentage(Settings.SellThresholdPercent);
            }
            else if (_planner is PomaPlanner pomaPlanner)
            {
                pomaPlanner.SocSoftMin = Settings.SocSoftMinPercent / 100.0;
                pomaPlanner.SocSoftMax = Settings.SocSoftMaxPercent / 100.0;
            }
        }

        public async Task Simulate(CancellationToken cancellationToken = default)
        {
            var priceUnit = new PriceUnit(Currencies.Euro, Units.MegaWattHour);
            DateTime t = TimeProvider.GetTime();
            TimeSpan replanningInterval = TimeSpan.FromMinutes(Settings.ReplanIntervalMinutes);
            TimeSpan planResolution = TimeSpan.FromMinutes(15);
            int planPeriods = (int)Math.Round(TimeSpan.FromHours(Settings.PlanningHorizonHours) / planResolution);

            PrepareMarketView(_start, _start + planPeriods * planResolution);
            var planningTask = _planner.UpdatePlan(_start, planResolution, planPeriods);
            await planningTask;

            DateTime lastPlanning = t;
            DateTime planEnd = t + planPeriods * planResolution;

            try
            {
                while(t < _end && SimulationEnabled && !cancellationToken.IsCancellationRequested)
                {
                    // Horizon and re-plan interval may be changed while running; ignore invalid edits.
                    if (Settings.Validate().Count == 0)
                    {
                        replanningInterval = TimeSpan.FromMinutes(Settings.ReplanIntervalMinutes);
                        planPeriods = (int)Math.Round(TimeSpan.FromHours(Settings.PlanningHorizonHours) / planResolution);
                    }

                    if (t - lastPlanning >= replanningInterval)
                    {
                        Console.Out.WriteLine($"Replanning...");

                        if (planningTask.IsCompleted)
                        {
                            ApplyPlannerSettings();
                            PrepareMarketView(t, t + planPeriods * planResolution);
                            planningTask = _planner.UpdatePlan(t, planResolution, planPeriods);
                            await planningTask;
                            planEnd = t + planPeriods * planResolution;
                        }

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

                    await _ems.UpdateState(tNext);

                    await Console.Out.WriteLineAsync($"{t}-{tNext:HH:mm}: Net charge = {_ems.GetChargeLevel()} SoC = {_ems.GetSoC().ToFraction():P1}");

                    if (_executor != null)
                    {
                        // Cash from this step's fills plus imbalance for what was delivered over [t, tNext).
                        var imbalance = _executor.SettleImbalance(t, tNext, _recorder.NetCharge[tNext], _market.Ts.EnergySellPrice[t]);
                        _pnlManager.RecordCashFlows(tNext, trades.BuyCost + imbalance.BuyCost, trades.SellIncome + imbalance.SellIncome);
                    }
                    else
                    {
                        // Energy traded over [t, tNext) is settled at the prices valid at t.
                        _pnlManager.UpdatePnL(tNext, new UnitPrice(_market.Ts.EnergyBuyPrice[t],priceUnit),new UnitPrice(_market.Ts.EnergySellPrice[t],priceUnit));
                    }

                    StepCompleted?.Invoke(this, BuildSnapshot(t, tNext, planEnd));

                    t = tNext;
                }
            }
            catch (OperationCanceledException)
            {
                // Stop requested — exit the simulation loop cleanly.
            }
        }

        public void Dispose()
        {
            _ems?.EndTransmission();
        }
    }
}
