using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator
{
    /// <summary>
    /// Turns a planner's plan into intraday trades. For every open 15-minute product between gate closure and the
    /// end of the current plan, it trades the difference between the planned and the held position with a
    /// market order. During delivery the battery follows the held (frozen) position, and any deviation is
    /// settled at the imbalance price.
    /// </summary>
    /// <remarks>Position = net energy bought for a product, MWh (+ = charging), i.e. -planned production x product length.</remarks>
    public class OrderBookExecutor
    {
        private static readonly TimeSpan ProductLength = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes);
        private const double MinTradeMWh = 0.01;

        private readonly SyntheticOrderBook _book;
        private readonly SimulationSettings _settings;
        private readonly DateTime _runEnd;
        private readonly Dictionary<DateTime, double> _positions = new();

        public OrderBookExecutor(SyntheticOrderBook book, SimulationSettings settings, DateTime runEnd)
        {
            _book = book;
            _settings = settings;
            _runEnd = runEnd;
        }

        public SyntheticOrderBook Book => _book;

        // Running totals for analysing execution.
        public double GrossBoughtMWh { get; private set; }
        public double GrossSoldMWh { get; private set; }
        public int TradeCount { get; private set; }
        public double ShortImbalanceMWh { get; private set; }
        public double LongImbalanceMWh { get; private set; }
        /// <summary>Net imbalance settlement (cost - income), EUR.</summary>
        public double ImbalanceNetCost { get; private set; }
        /// <summary>Net cash from intraday (order book) trades, income - cost, EUR.</summary>
        public double IntradayNetCash { get; private set; }
        public double DayAheadBoughtMWh { get; private set; }
        public double DayAheadSoldMWh { get; private set; }
        /// <summary>Sell volume bid in day-ahead but not accepted because the price cleared below 0 EUR/MWh.</summary>
        public double DayAheadRejectedSellMWh { get; private set; }
        /// <summary>Net cash from day-ahead settlement, income - cost, EUR.</summary>
        public double DayAheadNetCash { get; private set; }

        /// <summary>
        /// Settles day-ahead bids (average MW per product, + = buy) at the day-ahead price and adds the accepted
        /// volumes to the products' positions. Bids are price-independent: buys are always accepted, sells are bid
        /// from 0 EUR/MWh and so only accepted when the price clears at or above 0.
        /// </summary>
        /// <returns>The settlement's buy cost and sell income, both >= 0.</returns>
        public (double BuyCost, double SellIncome) SettleDayAhead(IReadOnlyDictionary<DateTime, double> bidMW, Func<DateTime, double> dayAheadPrice)
        {
            double buyCost = 0, sellIncome = 0;
            foreach (var (product, mw) in bidMW)
            {
                double volume = mw * ProductLength.TotalHours;
                if (Math.Abs(volume) < 1e-9) continue;
                double price = dayAheadPrice(product);
                if (volume < 0 && price < 0)
                {
                    DayAheadRejectedSellMWh -= volume;
                    continue;
                }

                _positions[product] = Position(product) + volume;
                var f = _fills.TryGetValue(product, out var prev) ? prev : default;
                _fills[product] = (f.Volume + Math.Abs(volume), f.Cash + Math.Abs(volume * price));
                if (volume > 0)
                {
                    DayAheadBoughtMWh += volume;
                    buyCost += volume * price;
                }
                else
                {
                    DayAheadSoldMWh -= volume;
                    sellIncome -= volume * price;
                }
            }
            DayAheadNetCash += sellIncome - buyCost;
            return (buyCost, sellIncome);
        }

        public IReadOnlyDictionary<DateTime, double> Positions => _positions;

        public double Position(DateTime product) => _positions.TryGetValue(product, out double v) ? v : 0;

        // Per product: total absolute fill volume and cash, for the volume-weighted average fill price.
        private readonly Dictionary<DateTime, (double Volume, double Cash)> _fills = new();

        /// <summary>Volume-weighted average price of all fills (buys and sells) in a product; NaN if never traded.</summary>
        public double AverageFillPrice(DateTime product) =>
            _fills.TryGetValue(product, out var f) && f.Volume > 0 ? f.Cash / f.Volume : double.NaN;

        public static DateTime ProductStart(DateTime t) => new DateTime(t.Ticks - t.Ticks % ProductLength.Ticks, t.Kind);

        /// <summary>The first product whose delivery starts at or after gate closure, i.e. the front product tradable at <paramref name="t"/>.</summary>
        public DateTime FirstTradableProduct(DateTime t)
        {
            DateTime gate = t.AddMinutes(_settings.GateClosureMinutes);
            DateTime first = ProductStart(gate);
            return first < gate ? first + ProductLength : first;
        }

        /// <summary>
        /// Trades towards the planned positions for products from gate closure up to <paramref name="planEnd"/>
        /// (and not beyond the end of the run, whose delivery would not be simulated).
        /// </summary>
        /// <returns>This step's buy cost and sell income, both >= 0.</returns>
        public (double BuyCost, double SellIncome) Trade(DateTime t, IBatteryPlanner planner, DateTime planEnd)
        {
            double buyCost = 0, sellIncome = 0;

            DateTime last = planEnd < _runEnd ? planEnd : _runEnd;

            for (DateTime product = FirstTradableProduct(t); product < last; product += ProductLength)
            {
                // Later products open no earlier than this one.
                if (!_book.HasOpened(product, t)) break;

                double target = -planner.GetPlannedProduction(product).CovertToUnit(Units.MegaWatt).Value * ProductLength.TotalHours;
                double delta = target - Position(product);
                if (Math.Abs(delta) < MinTradeMWh) continue;

                Fill fill = _book.ExecuteMarketOrder(product, delta, t);
                _positions[product] = Position(product) + fill.VolumeMWh;
                if (fill.CashEur < 0) buyCost -= fill.CashEur;
                else sellIncome += fill.CashEur;

                // |cash| = |volume| x fill price for both buys and sells.
                var f = _fills.TryGetValue(product, out var prev) ? prev : default;
                _fills[product] = (f.Volume + Math.Abs(fill.VolumeMWh), f.Cash + Math.Abs(fill.CashEur));

                TradeCount++;
                if (fill.VolumeMWh > 0) GrossBoughtMWh += fill.VolumeMWh;
                else GrossSoldMWh -= fill.VolumeMWh;
            }

            IntradayNetCash += sellIncome - buyCost;
            return (buyCost, sellIncome);
        }

        /// <summary>EMS setpoint during delivery (+ = charging): the held position spread evenly over its product.</summary>
        public Power DeliverySetpoint(DateTime t) => new Power(Position(ProductStart(t)) / ProductLength.TotalHours, Units.MegaWatt);

        /// <summary>
        /// Settles the difference between delivered and traded energy over [from, to) at the imbalance price:
        /// a shortfall is bought at realized price + penalty, a surplus sold at realized price - penalty.
        /// </summary>
        /// <param name="actualNetChargeMW">Average net grid charge over the interval (+ = charging).</param>
        /// <returns>The interval's imbalance cost and income, both >= 0.</returns>
        public (double BuyCost, double SellIncome) SettleImbalance(DateTime from, DateTime to, double actualNetChargeMW, double realizedPrice)
        {
            double hours = (to - from).TotalHours;
            double traded = Position(ProductStart(from)) / ProductLength.TotalHours * hours;
            double deviation = actualNetChargeMW * hours - traded;   // > 0: took more from the grid than bought

            if (deviation > 1e-9)
            {
                double cost = deviation * (realizedPrice + _settings.ImbalancePenalty);
                ShortImbalanceMWh += deviation;
                ImbalanceNetCost += cost;
                return (cost, 0);
            }
            if (deviation < -1e-9)
            {
                double income = -deviation * (realizedPrice - _settings.ImbalancePenalty);
                LongImbalanceMWh -= deviation;
                ImbalanceNetCost -= income;
                return (0, income);
            }
            return (0, 0);
        }
    }
}
