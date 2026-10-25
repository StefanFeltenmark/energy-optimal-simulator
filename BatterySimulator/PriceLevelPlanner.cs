
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class PriceLevelPlanner : IBatteryPlanner, IDayAheadBidder
    {
        private TimeSeries _plan;
        private Battery? _battery;
        private Random _r = new Random(297349);
        private TimeSeries _energyPriceForecastForecast;
        private ObservableTimeSeries _plannedSoC;
        private Percentage _buyPriceThreshold;
        private  Percentage _sellPriceThreshold;
        private TimeSpan _lookaAhead;

        public string Name => "PriceLevelPlanner";


        public PriceLevelPlanner()
        {
            _plan = new TimeSeries();
            _buyPriceThreshold = new Percentage(15);
            _sellPriceThreshold = new Percentage(15);
            _lookaAhead = TimeSpan.FromHours(6);
            _plannedSoC = new ObservableTimeSeries();
        }


        public TimeSeries Plan
        {
            get => _plan;
            set => _plan = value;
        }

        public TimeSeries EnergyPriceForecast
        {
            get => _energyPriceForecastForecast;
            set => _energyPriceForecastForecast = value;
        }

        public Percentage BuyPriceThreshold
        {
            get => _buyPriceThreshold;
            set => _buyPriceThreshold = value;
        }

        public Percentage SellPriceThreshold
        {
            get => _sellPriceThreshold;
            set => _sellPriceThreshold = value;
        }

        public Battery Battery1
        {
            get => _battery;
            set => _battery = value;
        }

        public TimeSpan LookaAhead
        {
            get => _lookaAhead;
            set => _lookaAhead = value;
        }


        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time], Units.MegaWatt);
        }

        public ObservableTimeSeries PlannedSoC
        {
            get { return _plannedSoC; }
        }

        public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            _plan = new TimeSeries(true, true);
            Apply(_plan, _energyPriceForecastForecast, planStart, resolution, nPeriods);
            return Task.CompletedTask;
        }

        /// <summary>
        /// The same rule on the day-ahead forecast: the bid day is split into blocks of <see cref="LookaAhead"/>,
        /// and in each block the battery buys at full power where the forecast is low and sells where it is high,
        /// with thresholds from that block's forecast range. Like the intraday plan, it ignores the state of charge.
        /// </summary>
        public Task<IReadOnlyDictionary<DateTime, double>> PlanDayAhead(DateTime planStart, MarketView view)
        {
            var biddable = view.Products.Where(q => q.MaxBuyMW > 0 || q.MaxSellMW > 0).ToList();
            var forecast = new TimeSeries(true, true) { IsBreakPointSeries = true };
            foreach (ProductQuote q in biddable) forecast.SetValueAt(q.Product, q.SellPrice);

            TimeSpan resolution = TimeSpan.FromMinutes(ProductQuote.ProductMinutes);
            var plan = new TimeSeries(true, true);
            for (int i = 0; i < biddable.Count; i += (int)(LookaAhead / resolution))
            {
                int n = Math.Min((int)(LookaAhead / resolution), biddable.Count - i);
                Apply(plan, forecast, biddable[i].Product, resolution, n);
            }

            IReadOnlyDictionary<DateTime, double> bid = biddable.ToDictionary(q => q.Product, q => -plan[q.Product]);
            return Task.FromResult(bid);
        }

        /// <summary>
        /// Writes full-power production (sell) or consumption (buy) into <paramref name="plan"/> for
        /// <paramref name="nPeriods"/> periods from <paramref name="start"/>, with buy and sell thresholds placed in
        /// the price range over the look-ahead window from <paramref name="start"/>.
        /// </summary>
        private void Apply(TimeSeries plan, TimeSeries prices, DateTime start, TimeSpan resolution, int nPeriods)
        {
            var window = prices.GetSubSeries(start, start + LookaAhead).Values().ToList();
            double maxPrice = window.Max();
            double minPrice = window.Min();

            var buyat = minPrice + _buyPriceThreshold.ToFraction() * (maxPrice - minPrice);
            var sellat = maxPrice - _sellPriceThreshold.ToFraction() * (maxPrice - minPrice);

            // Step through local clock times: the prices and the plan are keyed by them. (PlanningPeriod's
            // intervals are UTC and would shift both lookups by an hour.)
            for (int k = 0; k < nPeriods; k++)
            {
                DateTime t = start + k * resolution;
                double prod = 0.0;
                double price = prices[t];

                if(price <= buyat)
                {
                    prod = -Battery1.InitialCapacityC.CovertToUnit(Units.MegaWatt).Value;
                }
                else if (price > sellat)
                {
                    prod = Battery1.InitialCapacityC.CovertToUnit(Units.MegaWatt).Value;
                }

                plan.SetValueAt(t, prod);
            }
        }
    }
}
