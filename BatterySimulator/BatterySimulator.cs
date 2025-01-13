using MathNet.Numerics.Random;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Thermal.Quantities;

namespace BatterySimulator
{
    public class BatterySimulator
    {
        private DateTime _simulationTime;
        private bool _isRealTime;
        
        public BatterySimulator()
        {

        }

        public bool IsRealTime
        {
            get => _isRealTime;
            set => _isRealTime = value;
        }


        public void Simulate()
        {
            DateTime start = new DateTime(2025, 1, 10, 9,0,0, DateTimeKind.Local);

            Random r = new Random();

            EnergyMarket market = new EnergyMarket(Guid.NewGuid(), "EPEX Intraday");
            market.Ts.EnergySellPrice = TimeSeries.CreateTimeSeries(r.NextDoubleSequence().Take(100).ToArray(),start, TimeSpan.FromMinutes(15));
            double spread = 0.01;
            market.Ts.EnergyBuyPrice = market.Ts.EnergySellPrice + TimeSeries.CreateTimeSeries(market.Ts.EnergySellPrice.TimePoints(),spread);

            Battery b = new Battery
            {
                NominalChargeCapacity = new Power(10,Units.MegaWatt),
                NominalEnergyCapacity =  new Energy(100, Units.MegaWattHours),
                InitialSoHc = new Percentage(100),
                InitialSoHe = new Percentage(100)
            };

            BatteryState initialState = new BatteryState
            {
                EnergyContent = new Energy(50, Units.MegaWattHours),
                Capacity = new Energy(100, Units.MegaWattHours)
            };

            BatteryEMS batteryEms = new BatteryEMS(b, initialState);

            PlanningPeriod simulationPeriod = new PlanningPeriod(start, TimeSpan.FromMinutes(1), 24 * 60);

            // Generate a random plan or policy
            BatteryPlanner planner = new BatteryPlanner(batteryEms);
            planner.UpdatePlan(start);

            foreach (var simulationInterval in simulationPeriod.Intervals)
            {
                _simulationTime = simulationInterval.Start;

                // Implement plan/policy
                batteryEms.SetChargeLevel(planner.GetPlannedProduction(_simulationTime));
                
                // Update state
                batteryEms.UpdateState(simulationInterval.Length);

                Console.Out.WriteLine($"{_simulationTime}: Net charge = {batteryEms.GetChargeLevel()} SoC = {batteryEms.GetSoC().Value:P2}");

                if (_isRealTime)
                {
                    Thread.Sleep(simulationInterval.Length);
                    // test
                }

            }

            // Calculate market cost/revenue "settlement"

        }
    }
}
