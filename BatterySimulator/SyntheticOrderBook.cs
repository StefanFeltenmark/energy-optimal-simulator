using Powel.Optimal.MultiAsset.Domain.General.Data;

namespace BatterySimulator
{
    public readonly record struct BookLevel(double Price, double VolumeMWh);

    /// <summary>Result of a market order. VolumeMWh is signed (+ bought, - sold); CashEur is signed (- paid, + received).</summary>
    public readonly record struct Fill(double VolumeMWh, double CashEur)
    {
        public double AveragePrice => VolumeMWh == 0 ? double.NaN : -CashEur / VolumeMWh;
    }

    /// <summary>
    /// A synthetic continuous-intraday order book. Each product is a delivery period identified by its
    /// local delivery start; all open products trade at the same time. Trading in a product opens on the day
    /// before delivery (<see cref="TradingOpens"/>), and from then on its mid price is
    /// <c>day-ahead + MidVolatility x (F(now) - F(open)) + ProductNoiseVolatility x W_product(now - open)</c>,
    /// where F is a market-wide random walk in absolute time shared by all products and W is the product's own
    /// random walk, both frozen at delivery. Where the mid is at delivery is the product's realized intraday price
    /// (<see cref="RealizedPrice"/>), so the mids are martingales: their future moves cannot be predicted from
    /// anything visible now, and quarter-hours of the same hour can settle at different prices. The spread narrows
    /// and depth builds up as delivery approaches. Volume taken by market orders is removed from the book and
    /// replenished over time.
    /// </summary>
    public class SyntheticOrderBook
    {
        /// <summary>Mid-price random walks are generated on this grid and interpolated linearly in between.</summary>
        public const int PriceGridMinutes = 5;
        private const double MaxPathHours = 48;
        private static readonly DateTime FactorEpoch = new DateTime(2000, 1, 1);

        private readonly TimeSeries _dayAheadPrices;
        private readonly SimulationSettings _settings;
        private readonly Dictionary<DateTime, double[]> _paths = new();
        private readonly List<double> _factor = new() { 0 };
        private readonly long _factorOffset;
        private readonly Random _factorRng;
        private readonly Dictionary<(DateTime Product, bool AskSide), (double Volume, DateTime At)> _consumed = new();

        /// <param name="dayAheadPrices">Day-ahead prices keyed by local delivery time (the simulator's time axis).</param>
        public SyntheticOrderBook(TimeSeries dayAheadPrices, SimulationSettings settings)
        {
            _dayAheadPrices = dayAheadPrices;
            _settings = settings;

            // The market factor starts before trading opens for the run's first products (the day before the
            // start) at a point fixed by the run start, so it does not depend on query order.
            DateTime origin = settings.Start.Date.AddHours(-MaxPathHours - 24);
            _factorOffset = (long)((origin - FactorEpoch).TotalMinutes / PriceGridMinutes);
            _factorRng = new Random(unchecked(settings.OrderBookSeed * 7919 + 17));
        }

        /// <summary>When trading in <paramref name="product"/> opens: <see cref="SimulationSettings.TradingOpensHour"/> on the day before delivery.</summary>
        public DateTime TradingOpens(DateTime product) => product.Date.AddDays(-1).AddHours(_settings.TradingOpensHour);

        /// <summary>Whether trading in <paramref name="product"/> has opened at <paramref name="tradingTime"/> (gate closure is the executor's concern).</summary>
        public bool HasOpened(DateTime product, DateTime tradingTime) => tradingTime >= TradingOpens(product);

        public double Mid(DateTime product, DateTime tradingTime)
        {
            DateTime open = TradingOpens(product);
            DateTime now = tradingTime < product ? tradingTime : product;
            if (now <= open) return _dayAheadPrices[product];

            double[] path = Path(product);
            double x = Math.Min((now - open).TotalHours, MaxPathHours) * 60.0 / PriceGridMinutes;
            int i = (int)Math.Floor(x);
            double own = i + 1 < path.Length ? path[i] + (path[i + 1] - path[i]) * (x - i) : path[^1];

            double common = Factor(now) - Factor(open);

            return _dayAheadPrices[product] + _settings.MidVolatility * common + _settings.ProductNoiseVolatility * own;
        }

        /// <summary>The product's realized intraday price: its mid at delivery.</summary>
        public double RealizedPrice(DateTime product) => Mid(product, product);

        /// <summary>Market-wide standard random walk in absolute time, generated in order on a fixed grid.</summary>
        private double Factor(DateTime time)
        {
            double x = (time - FactorEpoch).TotalMinutes / PriceGridMinutes - _factorOffset;
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(time), "Order book queried more than 72 h before the run start.");

            int i = (int)Math.Floor(x);
            double stepStd = Math.Sqrt(PriceGridMinutes / 60.0);
            while (_factor.Count <= i + 1)
            {
                double u1 = 1 - _factorRng.NextDouble(), u2 = _factorRng.NextDouble();
                _factor.Add(_factor[^1] + stepStd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
            }
            return _factor[i] + (_factor[i + 1] - _factor[i]) * (x - i);
        }

        public double Spread(DateTime product, DateTime tradingTime)
        {
            double far = 1 - Math.Exp(-HoursToDelivery(product, tradingTime) / _settings.LiquidityBuildUpHours);
            return _settings.SpreadNearDelivery + (_settings.SpreadFarFromDelivery - _settings.SpreadNearDelivery) * far;
        }

        /// <summary>Sell orders available to a buyer, best (lowest) price first, after removing consumed volume.</summary>
        public IReadOnlyList<BookLevel> Asks(DateTime product, DateTime tradingTime) => Levels(product, tradingTime, askSide: true);

        /// <summary>Buy orders available to a seller, best (highest) price first, after removing consumed volume.</summary>
        public IReadOnlyList<BookLevel> Bids(DateTime product, DateTime tradingTime) => Levels(product, tradingTime, askSide: false);

        /// <summary>
        /// Read-only: the volume-weighted average price for buying (<paramref name="askSide"/>) or selling
        /// <paramref name="volumeMWh"/> now, and the total depth on that side. Price is NaN if the side is empty.
        /// </summary>
        public (double AveragePrice, double DepthMWh) QuoteForVolume(DateTime product, DateTime tradingTime, double volumeMWh, bool askSide)
        {
            var levels = Levels(product, tradingTime, askSide);
            double depth = levels.Sum(l => l.VolumeMWh);
            double remaining = Math.Min(volumeMWh, depth), filled = 0, cash = 0;
            foreach (BookLevel level in levels)
            {
                if (remaining <= 1e-9) break;
                double volume = Math.Min(remaining, level.VolumeMWh);
                filled += volume;
                cash += volume * level.Price;
                remaining -= volume;
            }
            double price = filled > 0 ? cash / filled : levels.Count > 0 ? levels[0].Price : double.NaN;
            return (price, depth);
        }

        /// <summary>
        /// Executes a market order for <paramref name="volumeMWh"/> (+ buy, - sell) by walking the book.
        /// Fills partially if the book is not deep enough.
        /// </summary>
        public Fill ExecuteMarketOrder(DateTime product, double volumeMWh, DateTime tradingTime)
        {
            if (Math.Abs(volumeMWh) < 1e-9) return default;

            bool buy = volumeMWh > 0;
            double remaining = Math.Abs(volumeMWh), filled = 0, cash = 0;
            foreach (BookLevel level in Levels(product, tradingTime, askSide: buy))
            {
                double volume = Math.Min(remaining, level.VolumeMWh);
                filled += volume;
                cash += volume * level.Price;
                remaining -= volume;
                if (remaining <= 1e-9) break;
            }

            _consumed[(product, buy)] = (Consumed(product, buy, tradingTime) + filled, tradingTime);
            return buy ? new Fill(filled, -cash) : new Fill(-filled, cash);
        }

        private List<BookLevel> Levels(DateTime product, DateTime tradingTime, bool askSide)
        {
            double tau = HoursToDelivery(product, tradingTime);
            double mid = Mid(product, tradingTime);
            double halfSpread = Spread(product, tradingTime) / 2;
            double levelVolume = _settings.LevelVolumeMWh *
                                 (_settings.FarDepthFraction + (1 - _settings.FarDepthFraction) * Math.Exp(-tau / _settings.LiquidityBuildUpHours));
            double consumed = Consumed(product, askSide, tradingTime);

            var levels = new List<BookLevel>(_settings.BookLevels);
            for (int i = 0; i < _settings.BookLevels; i++)
            {
                double price = askSide
                    ? mid + halfSpread + i * _settings.LevelPriceStep
                    : mid - halfSpread - i * _settings.LevelPriceStep;

                // Consumed volume is taken from the best levels first.
                double taken = Math.Min(levelVolume, consumed);
                consumed -= taken;
                if (levelVolume - taken > 1e-9) levels.Add(new BookLevel(price, levelVolume - taken));
            }
            return levels;
        }

        private double Consumed(DateTime product, bool askSide, DateTime tradingTime)
        {
            if (!_consumed.TryGetValue((product, askSide), out var c)) return 0;
            double elapsedMinutes = Math.Max(0, (tradingTime - c.At).TotalMinutes);
            return c.Volume * Math.Exp(-elapsedMinutes / _settings.LiquidityRecoveryMinutes);
        }

        private static double HoursToDelivery(DateTime product, DateTime tradingTime) => Math.Max(0, (product - tradingTime).TotalHours);

        /// <summary>Standard random walk in time since trading opened, starting at 0; deterministic per product.</summary>
        private double[] Path(DateTime product)
        {
            if (_paths.TryGetValue(product, out double[]? path)) return path;

            int n = (int)(MaxPathHours * 60.0 / PriceGridMinutes) + 1;
            path = new double[n];
            var rng = new Random(unchecked((int)((product.Ticks / TimeSpan.TicksPerMinute) * 2654435761L) ^ (_settings.OrderBookSeed * 97)));
            double stepStd = Math.Sqrt(PriceGridMinutes / 60.0);
            for (int k = 1; k < n; k++)
            {
                double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
                path[k] = path[k - 1] + stepStd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }

            _paths[product] = path;
            return path;
        }
    }
}
