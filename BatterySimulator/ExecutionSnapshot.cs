namespace BatterySimulator
{
    /// <summary>Position and plan for one 15-minute product, as average power (MW, + = charging).</summary>
    /// <param name="PositionMW">Held position; NaN with the single-price market model, which has no positions.</param>
    public readonly record struct ProductSnapshot(DateTime Product, double PositionMW, double PlannedMW);

    /// <summary>
    /// Immutable state published after every simulation step (<see cref="BatterySimulator.StepCompleted"/>), built on
    /// the simulation thread so observers such as the GUI never read planner or executor state while it changes.
    /// </summary>
    /// <param name="DeliveredMW">Average net grid charge over [IntervalStart, IntervalEnd) (+ = charging).</param>
    /// <param name="Products">Products from the one being delivered next up to the end of the current plan.</param>
    public sealed record ExecutionSnapshot(
        DateTime IntervalStart,
        DateTime IntervalEnd,
        double DeliveredMW,
        IReadOnlyList<ProductSnapshot> Products,
        bool HasOrderBook,
        double GrossTradedMWh,
        int TradeCount,
        double ShortImbalanceMWh,
        double LongImbalanceMWh,
        double ImbalanceNetCost);
}
