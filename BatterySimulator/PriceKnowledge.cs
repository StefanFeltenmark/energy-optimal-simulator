using Powel.Optimal.MultiAsset.Domain.General.Data;

namespace BatterySimulator
{
    /// <summary>
    /// What a trader knows at time t about the price of a 15-minute product: the day-ahead forecast until the
    /// day-ahead result is published (<see cref="SimulationSettings.DayAheadPublicationHour"/> on the day before
    /// delivery), then the day-ahead price, and the order book's mid once intraday trading in it has opened.
    /// Without a forecast (no day-ahead market) the day-ahead price counts as known all along, as before.
    /// </summary>
    public class PriceKnowledge
    {
        private readonly TimeSeries _dayAhead;
        private readonly TimeSeries? _forecast;
        private readonly SyntheticOrderBook? _book;
        private readonly double _publicationHour;

        /// <param name="dayAhead">Day-ahead prices keyed by local delivery time.</param>
        /// <param name="forecast">Day-ahead forecasts as known at gate closure, or null.</param>
        public PriceKnowledge(TimeSeries dayAhead, TimeSeries? forecast, SyntheticOrderBook? book, SimulationSettings settings)
        {
            _dayAhead = dayAhead;
            _forecast = forecast;
            _book = book;
            _publicationHour = settings.DayAheadPublicationHour;
        }

        /// <summary>When the day-ahead price (and accepted volume) for <paramref name="product"/> is published.</summary>
        public DateTime DayAheadPublished(DateTime product) => product.Date.AddDays(-1).AddHours(_publicationHour);

        public bool IsDayAheadKnown(DateTime product, DateTime t) => _forecast == null || t >= DayAheadPublished(product);

        /// <summary>The day-ahead price if published at <paramref name="t"/>, otherwise its forecast.</summary>
        public double DayAheadView(DateTime product, DateTime t) =>
            IsDayAheadKnown(product, t) ? _dayAhead[product] : _forecast![product];

        /// <summary>The best price estimate for <paramref name="product"/> available at <paramref name="t"/>.</summary>
        public double Expected(DateTime product, DateTime t) =>
            _book != null && _book.HasOpened(product, t) ? _book.Mid(product, t) : DayAheadView(product, t);
    }
}
