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

/// <summary>The market as it looks when a plan is made.</summary>
/// <param name="Products">Every product of the coming plan.</param>
/// <param name="MeanMidNext24h">
/// Average book mid over the products delivered in the next 24 h (EUR/MWh): the market's current view of what
/// energy is worth beyond the plan, e.g. for valuing energy left in storage at the end of the horizon.
/// </param>
public sealed record MarketView(IReadOnlyList<ProductQuote> Products, double MeanMidNext24h);

/// <summary>
/// A planner that can plan against the current order book and existing positions instead of a single price.
/// The simulator calls <see cref="SetMarketView"/> before every <see cref="IBatteryPlanner.UpdatePlan"/>.
/// </summary>
public interface IOrderBookAwarePlanner
{
    /// <param name="view">The current market, or null to plan against the reference price.</param>
    void SetMarketView(MarketView? view);
}

/// <summary>
/// A planner that can bid in the day-ahead auction. Bids are price-independent volumes, so the planner decides
/// only how much to buy or sell in each product of the coming delivery day.
/// </summary>
public interface IDayAheadBidder
{
    /// <summary>
    /// Plans the day-ahead bid. <paramref name="view"/> covers every product from <paramref name="planStart"/>:
    /// products already held have MaxBuyMW = MaxSellMW = 0 and their fixed position; the products to bid have
    /// zero position, the forecast as both prices and the battery's power as limits.
    /// <see cref="MarketView.MeanMidNext24h"/> is the forecast's mean over the bid day, for valuing energy left at its end.
    /// Must not change the planner's current intraday plan.
    /// </summary>
    /// <returns>Volume to bid per biddable product, as average power over the product (MW, + = buy).</returns>
    Task<IReadOnlyDictionary<DateTime, double>> PlanDayAhead(DateTime planStart, MarketView view);
}
