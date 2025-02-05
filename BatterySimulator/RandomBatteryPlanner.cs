
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class RandomBatteryPlanner : IBatteryPlanner
    {
        private TimeSeries _plan;
        private ObservableTimeSeries _plannedSoc;
        private Battery? _battery;
        private Random _r = new Random(297349);

        public RandomBatteryPlanner()
        {
            _plan = new TimeSeries();
        }

        public TimeSeries Plan
        {
            get => _plan;
            set => _plan = value;
        }

        public Battery? Battery1
        {
            get => _battery;
            set => _battery = value;
        }

        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time], Units.MegaWatt);
        }

        public ObservableTimeSeries PlannedSoC { get; set; }

       

        public async Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            // update
            _plan = new TimeSeries(true, true);
            
            PlanningPeriod planningPeriod = new PlanningPeriod(planStart, resolution, nPeriods);
            foreach (PlanningInterval simulationInterval in planningPeriod.Intervals)
            {
                var cap = Battery1.CapacityC().ConvertToUnit(Units.MegaWatt);
                var test = cap.Value - 2 * _r.NextDouble() * cap.Value; 
                _plan.SetValueAt(simulationInterval.Start, test);
            }
        }
    }
}
