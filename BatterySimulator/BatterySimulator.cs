using MathNet.Numerics.Random;
using OfficeOpenXml.FormulaParsing.Excel.Functions.DateTime;
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
        private DateTime _start;
        private bool _simulationEnabled;

        public BatterySimulator()
        {
            _delta = TimeSpan.FromSeconds(300);
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
            get => _delta;
            set => _delta = value;
        }

        public DateTime SimulationTime
        {
            get { return _time.GetTime(); }
        }

        public void SetUp(int nHours, int deltaSeconds)
        {
            Start = new DateTime(2025, 1, 10, 9,0,0, DateTimeKind.Local); 
            _end = _start + TimeSpan.FromHours(nHours);
            Delta = TimeSpan.FromSeconds(deltaSeconds);
            _time = new SimulationTimeProvider(_start, Delta);

            _recorder = new DataRecorder(_time);

            Random r = new Random();

            EnergyMarket market = new EnergyMarket(Guid.NewGuid(), "EPEX Intraday");
            market.Ts.EnergySellPrice = TimeSeries.CreateTimeSeries(r.NextDoubleSequence().Take(100).ToArray(),_start, TimeSpan.FromMinutes(15));
            double spread = 0.01;
            market.Ts.EnergyBuyPrice = market.Ts.EnergySellPrice + TimeSeries.CreateTimeSeries(market.Ts.EnergySellPrice.TimePoints(),spread);

            Battery b = new Battery
            {
                NominalChargeCapacity = new Power(10,Units.MegaWatt),
                NominalEnergyCapacity =  new Energy(10, Units.MegaWattHour),
                InitialSoHc = new Percentage(100),
                InitialSoHe = new Percentage(100)
            };

            BatteryState initialState = new BatteryState
            {
                EnergyContent = new Energy(5, Units.MegaWattHour),
                Capacity = new Energy(10, Units.MegaWattHour)
            };

            _ems = new BatteryEMS(b, initialState, _start);

            _recorder.Subscribe(_ems);

            // Generate a random plan or policy
            _planner = new BatteryPlanner(b);
            _planner.UpdatePlan(_start, TimeSpan.FromMinutes(15), 168);

        }

        public async Task Simulate()
        {
            
            while(_time.GetTime() < _end && SimulationEnabled)
            {
                // Implement plan/policy
                _ems.SetChargeLevel(_planner.GetPlannedProduction(_time.GetTime()));
                
                // Update state
                _ems.UpdateState(_time.GetTime());

                Console.Out.WriteLine($"{_time.GetTime()}: Net charge = {_ems.GetChargeLevel()} SoC = {_ems.GetSoC():P1}");

                Thread.Sleep(500);

                if (_isRealTime)
                {
                    Thread.Sleep(Delta);
                }

                _time.Increment();

            }

        }

        public void Dispose()
        {
            _ems.EndTransmission();
        }
    }
}
