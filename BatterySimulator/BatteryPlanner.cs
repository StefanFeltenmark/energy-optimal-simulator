using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Thermal.Quantities;

namespace BatterySimulator
{
    public class BatteryPlanner
    {
        private TimeSeries _plan;
        private BatteryEMS _battery;
        public BatteryPlanner(BatteryEMS battery)
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
            return _plan[time];
        }

        public void UpdatePlan(DateTime currentTime)
        {
            // update
            _plan = new TimeSeries(true, true);
            Random r = new Random();

            PlanningPeriod planningPeriod = new PlanningPeriod(currentTime, TimeSpan.FromMinutes(15), 20);
            foreach (PlanningInterval simulationInterval in planningPeriod.Intervals)
            {
                _plan.SetValueAt(simulationInterval.Start, _battery.Battery1.CapacityC().Value - 2*r.NextDouble()*_battery.Battery1.CapacityC().Value );
            }
        }
    }
}
