using MathNet.Numerics.Random;
using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator
{
    public class BatterySimulator : IDisposable
    {
        private SimulationTimeProvider _time;
        private bool _isRealTime;
        private DataRecorder _recorder;
        private BatteryEMS _ems;
        private IBatteryPlanner _planner;
        private IPriceForecaster _priceForecaster;
        private PnLManager _pnlManager;
        private DateTime _end;
        private DateTime _start;
        private bool _simulationEnabled;

        public BatterySimulator()
        {
        }

        public bool IsRealTime
        {
            get => _isRealTime;
            set => _isRealTime = value;
        }

        public DataRecorder Recorder
        {
            get => _recorder;
            set => _recorder = value;
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
            get => _time;
            set => _time = value;
        }

        public IPriceForecaster PriceForecaster
        {
            get => _priceForecaster;
            set => _priceForecaster = value;
        }


        public void SetUp(int nHours, int deltaSeconds)
        {
            _start = new DateTime(2025, 1, 10, 9,0,0, DateTimeKind.Local); 
            _end = _start + TimeSpan.FromHours(nHours);
            TimeSpan delta = TimeSpan.FromSeconds(deltaSeconds);
            TimeProvider = new SimulationTimeProvider(_start, delta);

            _recorder = new DataRecorder(TimeProvider);

            Random r = new Random(456345);

            EnergyMarket market = new EnergyMarket(Guid.NewGuid(), "EPEX Intraday");
            market.Ts.EnergySellPrice = TimeSeries.CreateTimeSeries(r.NextDoubleSequence().Take(100).ToArray(),_start, TimeSpan.FromMinutes(15));
            double spread = 0.01;
            market.Ts.EnergyBuyPrice = market.Ts.EnergySellPrice + TimeSeries.CreateTimeSeries(market.Ts.EnergySellPrice.TimePoints(),spread);

            // exakt 
            _priceForecaster = new PriceForecaster(market.Ts.EnergyBuyPrice);
            _priceForecaster.UpdateForecast(_start, TimeSpan.FromHours(nHours), TimeSpan.FromMinutes(15));

            Battery b = new Battery
            {
                NominalChargeCapacity = new Power(10,Units.MegaWatt),
                NominalEnergyCapacity =  new Energy(10, Units.MegaWattHour),
                InitialSoHc = new Percentage(100),
                InitialSoHe = new Percentage(100)
            };

            BatteryState initialState = new BatteryState
            {
                EnergyContent = new Energy(50, Units.MegaWattHour),
                Capacity = new Energy(100, Units.MegaWattHour)
            };


            _ems = new BatteryEMS(b, initialState, _start);

            _recorder.Subscribe(_ems);

            // Generate a random plan or policy
            //_planner = new RandomBatteryPlanner(b);
            //_planner.UpdatePlan(_start, TimeSpan.FromMinutes(15), 168);

            _planner = new PriceLevelPlanner(b);
            ((PriceLevelPlanner)_planner).EnergyPriceForecast = _priceForecaster.PriceForecast;

            _planner.UpdatePlan(_start, TimeSpan.FromMinutes(15), 168);

            _pnlManager = new PnLManager();

        }

        public async Task Simulate()
        {
            
            while(TimeProvider.GetTime() < _end && SimulationEnabled)
            {
                // Implement plan/policy
                _ems.SetChargeLevel(_planner.GetPlannedProduction(TimeProvider.GetTime()));
                
                // Update state
                await _ems.UpdateState(TimeProvider.GetTime());

                Console.Out.WriteLine($"{TimeProvider.GetTime()}: Net charge = {_ems.GetChargeLevel()} SoC = {_ems.GetSoC():P1}");

                Thread.Sleep(200);

                if (_isRealTime)
                {
                    Thread.Sleep(Delta);
                }

                TimeProvider.Increment();

            }

        }

        public void Dispose()
        {
            _ems.EndTransmission();
        }
    }
}
