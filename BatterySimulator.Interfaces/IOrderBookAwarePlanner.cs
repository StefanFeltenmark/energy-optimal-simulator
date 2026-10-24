namespace BatterySimulator.Interfaces;

/// <summary>
/// What a planner can do in one 15-minute product at the moment it plans (prices EUR/MWh, power MW).
/// Keyed by local delivery start, like the rest of the simulator.
/// </summary>
/// <param name="BuyPrice">Expected price for additional purchases (from the asks).</param>
/// <param name="SellPrice">Expected price for additional sales (from the bids).</param>
/// <param name="MaxBuyMW">Additional purchase possible, as average power over the product; 0 when not tradable.</param>
/// <param name="MaxSellMW">Additional sale possible, as average power over the product; 0 when not tradable.</param>
/// <param name="PositionMW">Already traded net purchase, as average power (+ = bought, to be charged).</param>
public readonly record struct ProductQuote(
    DateTime Product,
    double BuyPrice,
    double SellPrice,
    double MaxBuyMW,
    double MaxSellMW,
    double PositionMW)
{
    /// <summary>Length of an intraday product (delivery period), minutes.</summary>
    public const int ProductMinutes = 15;
}

/// <summary>
/// A planner that can plan against the current order book and existing positions instead of a single price.
/// The simulator calls <see cref="SetMarketView"/> before every <see cref="IBatteryPlanner.UpdatePlan"/>.
/// </summary>
public interface IOrderBookAwarePlanner
{
    /// <param name="quotes">Every product of the coming plan, or null to plan against the reference price.</param>
    void SetMarketView(IReadOnlyList<ProductQuote>? quotes);
}
