using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator.Interfaces;

public interface IBatteryPlanner
{
    Power GetPlannedProduction(DateTime time);

    TimeSeries PlannedSoC { get; }

    Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods);

}