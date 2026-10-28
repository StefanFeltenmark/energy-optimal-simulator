using Domain;
using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator.Interfaces;

public interface IBatteryPlanner
{
    /// <summary>Called once per run by the simulator's SetUp, before the first plan. The default needs nothing.</summary>
    void SetUp(PlannerContext context) { }

    /// <summary>
    /// Called after <see cref="SetUp"/> and before every re-plan, so these settings can be changed while a run
    /// goes. Planners take what applies to them; the default ignores them all.
    /// </summary>
    void ApplySettings(PlannerSettings settings) { }

    Power GetPlannedProduction(DateTime time);

    ObservableTimeSeries PlannedSoC { get; }

    Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods);

    string Name { get; }

}

/// <summary>What the simulator gives a planner for a run. Price series are keyed by local delivery time.</summary>
/// <param name="Battery">The battery, with its initial capacities computed.</param>
/// <param name="Market">The reference market: the price file's sell prices and buy prices (sell + spread).</param>
/// <param name="PriceForecast">The price forecast to plan against without a market view.</param>
/// <param name="InitialEnergyMWh">The battery's energy content at the start of the run.</param>
/// <param name="Wear">Battery wear to weigh against trading profit, or null to ignore wear.</param>
/// <param name="Ems">The battery's EMS, to follow its state through the run.</param>
public sealed record PlannerContext(
    Battery Battery,
    EnergyMarket Market,
    TimeSeries PriceForecast,
    double InitialEnergyMWh,
    BatteryWearModel? Wear,
    IObservable<BatteryState> Ems);

/// <summary>Planner settings that may change while a simulation runs.</summary>
/// <param name="LookAhead">PriceLevel: the window its price thresholds are computed over.</param>
/// <param name="BuyThresholdPercent">PriceLevel: buy in the bottom this % of the window's price range.</param>
/// <param name="SellThresholdPercent">PriceLevel: sell in the top this % of the window's price range.</param>
/// <param name="SocSoftMinPercent">POMA: soft lower SoC limit, %.</param>
/// <param name="SocSoftMaxPercent">POMA: soft upper SoC limit, %.</param>
/// <param name="RequestDirectory">POMA: directory to write every request to as Gateway-style JSON, or empty for none.</param>
public sealed record PlannerSettings(
    TimeSpan LookAhead,
    double BuyThresholdPercent,
    double SellThresholdPercent,
    double SocSoftMinPercent,
    double SocSoftMaxPercent,
    string RequestDirectory = "");
