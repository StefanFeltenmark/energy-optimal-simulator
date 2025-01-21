using MathNet.Numerics.Random;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.Common;
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
        private BatteryPlanner _planner;
        private TimeSpan _delta;
        private DateTime _end;
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

        public void SetUp(int nHours, int deltaSeconds)
        {
            DateTime start = new DateTime(2025, 1, 10, 9,0,0, DateTimeKind.Local); 
            _end = start + TimeSpan.FromHours(nHours);
            _delta = TimeSpan.FromSeconds(deltaSeconds);
            _time = new SimulationTimeProvider(start, _delta);

            _recorder = new DataRecorder(_time);

            Random r = new Random();

            EnergyMarket market = new EnergyMarket(Guid.NewGuid(), "EPEX Intraday");
            market.Ts.EnergySellPrice = TimeSeries.CreateTimeSeries(r.NextDoubleSequence().Take(100).ToArray(),start, TimeSpan.FromMinutes(15));
            double spread = 0.01;
            market.Ts.EnergyBuyPrice = market.Ts.EnergySellPrice + TimeSeries.CreateTimeSeries(market.Ts.EnergySellPrice.TimePoints(),spread);

            Battery b = new Battery
            {
                NominalChargeCapacity = new Power(10,Units.MegaWatt),
                NominalEnergyCapacity =  new Energy(100, Units.MegaWattHour),
                InitialSoHc = new Percentage(100),
                InitialSoHe = new Percentage(100)
            };

            BatteryState initialState = new BatteryState
            {
                EnergyContent = new Energy(50, Units.MegaWattHour),
                Capacity = new Energy(100, Units.MegaWattHour)
            };

            _ems = new BatteryEMS(b, initialState, start);

            _recorder.Subscribe(_ems);

            _recorder.EnergyContent.CollectionChanged += EnergyContent_CollectionChanged;

            //PlanningPeriod simulationPeriod = new PlanningPeriod(start, TimeSpan.FromMinutes(1), 24 * 60);

            // Generate a random plan or policy
            _planner = new BatteryPlanner(b);
            _planner.UpdatePlan(start);

        }

        private void EnergyContent_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Console.Out.WriteLine($"Got this: {e.Action}");
        }
        

        public async Task Simulate()
        {
            
            while(_time.GetTime() < _end && SimulationEnabled)
            {
                // Implement plan/policy
                _ems.SetChargeLevel(_planner.GetPlannedProduction(_time.GetTime()));
                
                // Update state
                _ems.UpdateState(_time.GetTime());

                Console.Out.WriteLine($"{_time.GetTime()}: Net charge = {_ems.GetChargeLevel()} SoC = {_ems.GetSoC().Value:P2}");

                Thread.Sleep(100);

                if (_isRealTime)
                {
                    Thread.Sleep(_delta);
                    // test
                }

                _time.Increment();

            }

            Recorder.Unsubscribe();

            // Calculate market cost/revenue "settlement"

        }

        public void Dispose()
        {
            _ems.EndTransmission();
        }
    }
}
