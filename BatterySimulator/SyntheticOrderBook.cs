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
    /// <c>day-ahead + opening deviation + integral from open to now of s(delivery - u) x (MidVolatility dF(u) + ProductNoiseVolatility dW_product(u))</c>.
    /// The opening deviation is a jump away from the day-ahead price when trading opens, partly common to the
    /// delivery day and partly per product. F is a market-wide random walk in absolute time shared by all
    /// products; the product walks W are correlated across the delivery day's consecutive quarter-hours (an AR(1)
    /// chain of their increments). s raises the volatility towards delivery. All of it freezes at delivery, where
    /// the mid is the product's realized intraday price (<see cref="RealizedPrice"/>). Every term has mean zero
    /// given what is visible before it, so the mids are martingales: their future moves cannot be predicted from
    /// anything visible now. The defaults are fitted to SE3 auction and index prices by
    /// scripts/calibrate_order_book.py. The spread narrows and depth builds up as delivery approaches. Volume taken
    /// by market orders is removed from the book and replenished over time.
    /// </summary>
    public class SyntheticOrderBook
    {
        /// <summary>Mid-price random walks are generated on this grid and interpolated linearly in between.</summary>
        public const int PriceGridMinutes = 5;
        private const double MaxPathHours = 48;
        private static readonly DateTime FactorEpoch = new DateTime(2000, 1, 1);

        private readonly TimeSeries _dayAheadPrices;
        private readonly SimulationSettings _settings;
        private readonly Dictionary<DateTime, ProductPath> _paths = new();
        private readonly Dictionary<DateTime, ChainState> _chains = new();
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
            if (now < open) return _dayAheadPrices[product];

            ProductPath path = Path(product);
            double x = Math.Min((now - open).TotalHours, MaxPathHours) * 60.0 / PriceGridMinutes;

            return _dayAheadPrices[product] + path.Opening
                   + _settings.MidVolatility * Interpolate(path.Common, x) + _settings.ProductNoiseVolatility * Interpolate(path.Own, x);
        }

        private static double Interpolate(double[] path, double x)
        {
            int i = (int)Math.Floor(x);
            return i + 1 < path.Length ? path[i] + (path[i + 1] - path[i]) * (x - i) : path[^1];
        }

        /// <summary>Volatility multiplier <paramref name="hoursToDelivery"/> before delivery: 1 far from delivery, <see cref="SimulationSettings.VolatilityAtDeliveryFactor"/> at delivery.</summary>
        public double VolatilityScale(double hoursToDelivery)
        {
            double factor = _settings.VolatilityAtDeliveryFactor;
            return Math.Sqrt(1 + (factor * factor - 1) * Math.Exp(-Math.Max(0, hoursToDelivery) / _settings.VolatilityBuildUpHours));
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
                _factor.Add(_factor[^1] + stepStd * StandardNormal(_factorRng));
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

        /// <summary>
        /// A product's opening deviation (EUR/MWh) and its common and own walks since trading opened, on the price
        /// grid from the opening (0) to delivery, with the steps scaled by <see cref="VolatilityScale"/>.
        /// </summary>
        private sealed record ProductPath(double Opening, double[] Own, double[] Common);

        /// <summary>
        /// A product's standard normal walk increments (one per price-grid step after the opening) and opening
        /// deviation, correlated with the previous quarter-hours of its delivery day; OpeningChain is the opening's
        /// AR(1) part.
        /// </summary>
        private sealed record ChainState(double[] Steps, double OpeningChain, double Opening);

        private ProductPath Path(DateTime product)
        {
            if (_paths.TryGetValue(product, out ProductPath? path)) return path;

            DateTime open = TradingOpens(product);
            double gridHours = PriceGridMinutes / 60.0;
            double life = Math.Min((product - open).TotalHours, MaxPathHours);
            int n = (int)Math.Ceiling(life / gridHours - 1e-9);
            ChainState chain = Chain(product);

            var own = new double[n + 1];
            var common = new double[n + 1];
            for (int k = 0; k < n; k++)
            {
                double scale = VolatilityScale(life - (k + 0.5) * gridHours);
                own[k + 1] = own[k] + scale * Math.Sqrt(gridHours) * chain.Steps[k];
                if (_settings.MidVolatility != 0)
                    common[k + 1] = common[k] + scale * (Factor(open.AddMinutes((k + 1) * PriceGridMinutes)) - Factor(open.AddMinutes(k * PriceGridMinutes)));
            }

            double opening = _settings.OpeningCommonStd * DayDeviation(product.Date) + _settings.OpeningProductStd * chain.Opening;
            path = new ProductPath(opening, own, common);
            _paths[product] = path;
            return path;
        }

        /// <summary>
        /// Builds the AR(1) chains from the first quarter-hour of <paramref name="product"/>'s delivery day (all of
        /// whose products open together) up to it: each product's walk increments and opening deviation are the
        /// previous product's times the correlation plus fresh noise, so they stay standard normal.
        /// </summary>
        private ChainState Chain(DateTime product)
        {
            if (_chains.TryGetValue(product, out ChainState? state)) return state;

            TimeSpan quarter = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes);
            DateTime first = product;
            while (first - quarter >= product.Date && !_chains.ContainsKey(first - quarter)) first -= quarter;

            double phi = _settings.ProductNoiseCorrelation, phiOpen = _settings.OpeningProductCorrelation, share = _settings.OpeningQuarterShare;
            for (DateTime p = first; p <= product; p += quarter)
            {
                var (steps, openingChain, independent) = Innovations(p);
                if (p - quarter >= p.Date && _chains.TryGetValue(p - quarter, out ChainState? previous))
                {
                    for (int k = 0; k < steps.Length; k++)
                        steps[k] = phi * previous.Steps[k] + Math.Sqrt(1 - phi * phi) * steps[k];
                    openingChain = phiOpen * previous.OpeningChain + Math.Sqrt(1 - phiOpen * phiOpen) * openingChain;
                }
                state = new ChainState(steps, openingChain, Math.Sqrt(share) * independent + Math.Sqrt(1 - share) * openingChain);
                _chains[p] = state;
            }
            return state!;
        }

        /// <summary>A product's own independent standard normals: walk increments, then two for the opening; deterministic per product.</summary>
        private (double[] Steps, double OpeningChain, double OpeningIndependent) Innovations(DateTime product)
        {
            var rng = new Random(unchecked((int)((product.Ticks / TimeSpan.TicksPerMinute) * 2654435761L) ^ (_settings.OrderBookSeed * 97)));
            var steps = new double[(int)(MaxPathHours * 60.0 / PriceGridMinutes)];
            for (int k = 0; k < steps.Length; k++) steps[k] = StandardNormal(rng);
            return (steps, StandardNormal(rng), StandardNormal(rng));
        }

        /// <summary>The standard normal opening deviation common to all products of delivery day <paramref name="day"/>.</summary>
        private double DayDeviation(DateTime day) =>
            StandardNormal(new Random(unchecked((int)(day.Ticks / TimeSpan.TicksPerDay) * 40503 ^ (_settings.OrderBookSeed * 7907 + 3))));

        private static double StandardNormal(Random rng)
        {
            double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
