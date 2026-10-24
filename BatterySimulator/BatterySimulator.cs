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

            _sleepTime = TimeSpan.FromMilliseconds(settings.SleepMilliseconds);

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

            var planningTask = _planner.UpdatePlan(_start, planResolution, planPeriods);
            await planningTask;

            DateTime lastPlanning = t;

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
                            planningTask = _planner.UpdatePlan(t, planResolution, planPeriods);
                            await planningTask;
                        }

                        lastPlanning = t;
                    }

                    // Implement plan/policy for the interval [t, t + Delta)
                    _ems.SetChargeLevel(-_planner.GetPlannedProduction(t));

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

                    // Energy traded over [t, tNext) is settled at the prices valid at t.
                    _pnlManager.UpdatePnL(tNext, new UnitPrice(_market.Ts.EnergyBuyPrice[t],priceUnit),new UnitPrice(_market.Ts.EnergySellPrice[t],priceUnit));

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
