namespace BatterySimulator
{
    /// <summary>Position and plan for one 15-minute product, as average power (MW, + = charging).</summary>
    /// <param name="PositionMW">Held position; NaN with the single-price market model, which has no positions.</param>
    /// <param name="AverageFillPrice">Volume-weighted average fill price (EUR/MWh); NaN if not traded (or single price).</param>
    public readonly record struct ProductSnapshot(DateTime Product, double PositionMW, double PlannedMW, double AverageFillPrice);

    /// <summary>Top of the book for one product at one trading time (EUR/MWh). Bid/ask are NaN when that side is empty.</summary>
    /// <param name="ReferencePrice">The product's realized intraday price, i.e. its mid at delivery (hindsight, for display only).</param>
    public readonly record struct BookQuote(DateTime Product, double BestBid, double BestAsk, double Mid, double ReferencePrice);

    /// <summary>
    /// Immutable state published after every simulation step (<see cref="BatterySimulator.StepCompleted"/>), built on
    /// the simulation thread so observers such as the GUI never read planner or executor state while it changes.
    /// </summary>
    /// <param name="DeliveredMW">Average net grid charge over [IntervalStart, IntervalEnd) (+ = charging).</param>
    /// <param name="Products">Products from the one being delivered next up to the end of the current plan.</param>
    /// <param name="Quotes">
    /// Top of the book at IntervalEnd for the current delivery day's products and, once open, the next day's;
    /// bid/ask are NaN for products that cannot be traded. Empty without the order book.
    /// </param>
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
        double ImbalanceNetCost,
        IReadOnlyList<BookQuote> Quotes,
        bool HasDayAhead = false,
        double DayAheadNetCash = 0,
        double IntradayNetCash = 0);
}
