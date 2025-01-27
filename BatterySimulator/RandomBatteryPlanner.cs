
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class RandomBatteryPlanner : IBatteryPlanner
    {
        private TimeSeries _plan;
        private Battery _battery;
        private Random _r = new Random(297349);

        public RandomBatteryPlanner(Battery battery)
        {
            _plan = new TimeSeries();
            _battery = battery;
        }

        public TimeSeries Plan
        {
            get => _plan;
            set => _plan = value;
        }

        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time], Units.MegaWatt);
        }

        public void UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            // update
            _plan = new TimeSeries(true, true);
            
            PlanningPeriod planningPeriod = new PlanningPeriod(planStart, resolution, nPeriods);
            foreach (PlanningInterval simulationInterval in planningPeriod.Intervals)
            {
                var cap = _battery.CapacityC().ConvertToUnit(Units.MegaWatt);
                var test = cap.Value - 2 * _r.NextDouble() * cap.Value; 
                _plan.SetValueAt(simulationInterval.Start, test);
            }
        }
    }
}
