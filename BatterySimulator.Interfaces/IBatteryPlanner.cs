using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator.Interfaces;

public interface IBatteryPlanner
{
    Power GetPlannedProduction(DateTime time);

    Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods);

}