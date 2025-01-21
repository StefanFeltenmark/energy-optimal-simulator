
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class BatteryPlanner
    {
        private TimeSeries _plan;
        private Battery _battery;
        public BatteryPlanner(Battery battery)
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

        public void UpdatePlan(DateTime currentTime)
        {
            // update
            _plan = new TimeSeries(true, true);
            Random r = new Random();

            PlanningPeriod planningPeriod = new PlanningPeriod(currentTime, TimeSpan.FromMinutes(15), 20);
            foreach (PlanningInterval simulationInterval in planningPeriod.Intervals)
            {
                var cap = _battery.CapacityC().ConvertToUnit(Units.MegaWatt);
                var test = cap.Value - 2 * r.NextDouble() * cap.Value; 
                _plan.SetValueAt(simulationInterval.Start, test);
            }
        }
    }
}
