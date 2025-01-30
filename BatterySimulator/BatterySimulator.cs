using MathNet.Numerics.Random;
using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using System;

namespace BatterySimulator
{
    public class BatterySimulator : IDisposable
    {
        private SimulationTimeProvider _timeProvider;
        private bool _isRealTime;
        private BatteryDataRecorder _recorder;
        private BatteryEMS _ems;
        private IBatteryPlanner _planner;
        private IPriceForecaster _priceForecaster;
        private PnLManager _pnlManager;
        private DateTime _end;
        private DateTime _start;
        private bool _simulationEnabled;
        private EnergyMarket _market;
        private Battery _battery;
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

        public BatteryDataRecorder Recorder
        {
            get => _recorder;
            
        }

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

        public PnLManager PnlManager
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


        public void SetUp(int nHours, int deltaSeconds)
        {
            _start = new DateTime(2018, 1, 1, 9,0,0, DateTimeKind.Local); 
            _end = _start + TimeSpan.FromHours(nHours);
            TimeSpan delta = TimeSpan.FromSeconds(deltaSeconds);
            TimeProvider = new SimulationTimeProvider(_start, delta);
            
            _recorder = new BatteryDataRecorder(_timeProvider);

            _market = new EnergyMarket(Guid.NewGuid(), "EPEX Intraday");

            //
            string filename = "C:\\Users\\stefan.feltenmark\\Documents\\energy-optimal-simulator\\Data\\Prices\\elspot-prices_2018_hourly_eur.csv";
          
            var prices  = SpotPriceReader.ReadFile(filename);
            _market.Ts.EnergySellPrice = prices["SE3"];
            _market.Ts.EnergySellPrice.IsBreakPointSeries = true;
           
            //_market.Ts.EnergySellPrice = TimeSeries.CreateTimeSeries(r.NextDoubleSequence().Take(nPeriods).Select(d=>d*100.0 + 20).ToArray(),_start, marketResolution);
            double spread = 0.01;
            _market.Ts.EnergyBuyPrice = _market.Ts.EnergySellPrice + TimeSeries.CreateTimeSeries(_market.Ts.EnergySellPrice.TimePoints(),spread);

            
            // exact 
            _priceForecaster = new PriceForecaster(_market.Ts.EnergyBuyPrice, a: 0, b: 0);
            _priceForecaster.UpdateForecast(_start, TimeSpan.FromHours(nHours), TimeSpan.FromMinutes(15));

            Battery1 = new Battery
            {
                NominalChargeCapacity = new Power(10,Units.MegaWatt),
                NominalEnergyCapacity =  new Energy(20, Units.MegaWattHour),
                InitialSoHc = new Percentage(100),
                InitialSoHe = new Percentage(100),
                ChargeEfficiency = 0.95,
                DischargeEfficiency = 0.95
            };

            BatteryState initialState = new BatteryState
            {
                EnergyContent = new Energy(10, Units.MegaWattHour),
                Capacity = new Energy(20, Units.MegaWattHour)
            };


            _ems = new BatteryEMS(Battery1, initialState, _start);

            _recorder.Subscribe(_ems);

            // Generate a plan
            if (_planner is PriceLevelPlanner planner)
            {
                planner.EnergyPriceForecast = _priceForecaster.PriceForecast;
                planner.Battery1 = _battery;
                planner.LookaAhead = TimeSpan.FromHours(4);
            }

            _pnlManager = new PnLManager(_recorder);
            _pnlManager.SetUp(_start);

            _sleepTime = TimeSpan.FromMilliseconds(200);

        }

        public async Task Simulate()
        {
            var priceUnit = new PriceUnit(Currencies.Euro, Units.MegaWattHour);
            DateTime t = TimeProvider.GetTime();
            TimeSpan replanningInterval = TimeSpan.FromMinutes(60);
            
            _planner.UpdatePlan(_start, TimeSpan.FromMinutes(15), 168);
            
            DateTime lastPlanning = t;

            while(t < _end && SimulationEnabled)
            {

                if (t - lastPlanning >= replanningInterval)
                {
                    await Console.Out.WriteLineAsync($"Replanning...");
                    _planner.UpdatePlan(t, TimeSpan.FromMinutes(15), 168);
                    lastPlanning = t;
                }

                // Implement plan/policy
                _ems.SetChargeLevel(_planner.GetPlannedProduction(t));
                
                // Update state
                await _ems.UpdateState(t);

                await Console.Out.WriteLineAsync($"{t}: Net charge = {_ems.GetChargeLevel()} SoC = {_ems.GetSoC():P1}");

                Thread.Sleep((int) _sleepTime.TotalMilliseconds);

                if (_isRealTime)
                {
                    Thread.Sleep(Delta);
                }


                PnlManager.UpdatePnL(t, new UnitPrice(_market.Ts.EnergyBuyPrice[t],priceUnit),new UnitPrice(_market.Ts.EnergySellPrice[t],priceUnit));
                
                TimeProvider.Increment();

                t = TimeProvider.GetTime();
            }



        }

        public void Dispose()
        {
            _ems.EndTransmission();
        }
    }
}
