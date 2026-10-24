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
    /// local delivery start; all products trade at the same time. A product's mid price is
    /// <c>realized + MidVolatility x (F(now) - F(delivery)) + ProductNoiseVolatility x W_product(time to delivery)</c>,
    /// where F is a market-wide random walk in absolute time shared by all products and W is the product's own
    /// random walk. Both terms vanish at delivery, so the mid ends exactly at the realized price, and neighbouring
    /// products move almost together. The spread narrows and depth builds up as delivery approaches. Volume taken
    /// by market orders is removed from the book and replenished over time.
    /// </summary>
    public class SyntheticOrderBook
    {
        private const double PathGridMinutes = 5;
        private const double MaxPathHours = 48;
        private static readonly DateTime FactorEpoch = new DateTime(2000, 1, 1);

        private readonly TimeSeries _realizedPrices;
        private readonly SimulationSettings _settings;
        private readonly Dictionary<DateTime, double[]> _paths = new();
        private readonly List<double> _factor = new() { 0 };
        private readonly long _factorOffset;
        private readonly Random _factorRng;
        private readonly Dictionary<(DateTime Product, bool AskSide), (double Volume, DateTime At)> _consumed = new();

        /// <param name="realizedPrices">Realized prices keyed by local delivery time (the simulator's time axis).</param>
        public SyntheticOrderBook(TimeSeries realizedPrices, SimulationSettings settings)
        {
            _realizedPrices = realizedPrices;
            _settings = settings;

            // The market factor starts well before the run (products can be quoted up to MaxPathHours ahead of
            // delivery) at a point fixed by the run start, so it does not depend on query order.
            DateTime origin = settings.Start.Date.AddHours(-MaxPathHours - 24);
            _factorOffset = (long)((origin - FactorEpoch).TotalMinutes / PathGridMinutes);
            _factorRng = new Random(unchecked(settings.OrderBookSeed * 7919 + 17));
        }

        public double Mid(DateTime product, DateTime tradingTime)
        {
            double[] path = Path(product);
            double x = Math.Min(HoursToDelivery(product, tradingTime), MaxPathHours) * 60 / PathGridMinutes;
            int i = (int)Math.Floor(x);
            double own = i + 1 < path.Length ? path[i] + (path[i + 1] - path[i]) * (x - i) : path[^1];

            // Market-wide shock still to come before delivery; clamped like the product path to MaxPathHours.
            DateTime from = tradingTime < product ? tradingTime : product;
            if ((product - from).TotalHours > MaxPathHours) from = product.AddHours(-MaxPathHours);
            double common = Factor(from) - Factor(product);

            return _realizedPrices[product] + _settings.MidVolatility * common + _settings.ProductNoiseVolatility * own;
        }

        /// <summary>Market-wide standard random walk in absolute time, generated in order on a fixed grid.</summary>
        private double Factor(DateTime time)
        {
            double x = (time - FactorEpoch).TotalMinutes / PathGridMinutes - _factorOffset;
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(time), "Order book queried more than 72 h before the run start.");

            int i = (int)Math.Floor(x);
            double stepStd = Math.Sqrt(PathGridMinutes / 60);
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

        /// <summary>Standard random walk in time-to-delivery, starting at 0 at delivery; deterministic per product.</summary>
        private double[] Path(DateTime product)
        {
            if (_paths.TryGetValue(product, out double[]? path)) return path;

            int n = (int)(MaxPathHours * 60 / PathGridMinutes) + 1;
            path = new double[n];
            var rng = new Random(unchecked((int)((product.Ticks / TimeSpan.TicksPerMinute) * 2654435761L) ^ (_settings.OrderBookSeed * 97)));
            double stepStd = Math.Sqrt(PathGridMinutes / 60);
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
