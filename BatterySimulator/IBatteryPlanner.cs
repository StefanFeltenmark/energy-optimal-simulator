using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator;

public interface IBatteryPlanner
{
    Power GetPlannedProduction(DateTime time);

    void UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods);

}