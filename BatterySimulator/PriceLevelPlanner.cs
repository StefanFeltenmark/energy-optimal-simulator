using BatterySimulator.Interfaces;
using Domain;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    /// <summary>
    /// Baseline strategy: buy at full power where the price is in the bottom <see cref="BuyPriceThreshold"/> of the
    /// price range over the look-ahead window, sell where it is in the top <see cref="SellPriceThreshold"/>, and
    /// only if the round trip pays for the charge and discharge losses (and wear, see <see cref="WearCostPerMWh"/>). The plan follows the battery's energy from
    /// its current state (it observes the EMS), so it never plans to charge a full battery or discharge an empty
    /// one, and products that can no longer be traded keep their positions. With a market view (order book) it
    /// buys at the asks and sells at the bids, with thresholds from the expected prices; without one it plans
    /// against <see cref="EnergyPriceForecast"/>.
    /// </summary>
    public class PriceLevelPlanner : IBatteryPlanner, IOrderBookAwarePlanner, IDayAheadBidder, IObserver<BatteryState>
    {
        private TimeSeries _plan;
        private Battery? _battery;
        private TimeSeries _energyPriceForecastForecast;
        private ObservableTimeSeries _plannedSoC;
        private Percentage _buyPriceThreshold;
        private  Percentage _sellPriceThreshold;
        private TimeSpan _lookaAhead;
        private MarketView? _marketView;

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

        /// <summary>Wear cost per MWh charged or discharged, EUR/MWh; a round trip must also pay for it. 0 ignores wear.</summary>
        public double WearCostPerMWh { get; set; }

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

        /// <summary>The battery's energy content, MWh: set at the start of a run, then updated by the EMS.</summary>
        public double EnergyContentMWh { get; set; }


        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time], Units.MegaWatt);
        }

        public ObservableTimeSeries PlannedSoC
        {
            get { return _plannedSoC; }
        }

        public void SetMarketView(MarketView? view) => _marketView = view;

        public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            List<Slot> slots;
            List<(DateTime Time, double Price)> expected;
            if (_marketView != null && _marketView.Products.Count > 0)
            {
                slots = _marketView.Products.Select(FromQuote).ToList();
                expected = slots.Select(s => (s.Time, s.Expected)).ToList();
            }
            else
            {
                double power = PowerMW;
                // Step through local clock times: the prices and the plan are keyed by them. (PlanningPeriod's
                // intervals are UTC and would shift both lookups by an hour.)
                slots = Enumerable.Range(0, nPeriods).Select(k => planStart + k * resolution)
                    .Select(t => new Slot(t, _energyPriceForecastForecast[t], _energyPriceForecastForecast[t], _energyPriceForecastForecast[t], -power, power))
                    .ToList();
                // The window looks beyond the plan's end, as far as the forecast goes.
                DateTime windowEnd = planStart + nPeriods * resolution + LookaAhead;
                expected = _energyPriceForecastForecast.GetSubSeries(planStart, windowEnd).TimePoints()
                    .Select(t => (t, _energyPriceForecastForecast[t])).ToList();
            }

            var plan = new TimeSeries(true, true);
            foreach (var (time, production) in Schedule(slots, expected, resolution.TotalHours))
                plan.SetValueAt(time, production);
            _plan = plan;
            return Task.CompletedTask;
        }

        /// <summary>
        /// The same rule on the day-ahead view: products already held keep their positions, and the bid products
        /// buy where the forecast is low and sell where it is high within the look-ahead window, as far as the
        /// battery's energy, followed through the held products, allows.
        /// </summary>
        public Task<IReadOnlyDictionary<DateTime, double>> PlanDayAhead(DateTime planStart, MarketView view)
        {
            var slots = view.Products.Select(FromQuote).ToList();
            var schedule = Schedule(slots, slots.Select(s => (s.Time, s.Expected)).ToList(), ProductQuote.ProductMinutes / 60.0);

            IReadOnlyDictionary<DateTime, double> bid = view.Products
                .Zip(schedule, (quote, planned) => (quote, planned.Production))
                .Where(x => x.quote.MaxBuyMW > 0 || x.quote.MaxSellMW > 0)
                .ToDictionary(x => x.quote.Product, x => -x.Production - x.quote.PositionMW);
            return Task.FromResult(bid);
        }

        /// <summary>One period to plan: the prices to buy and sell at, the expected price and the possible production (MW, + = discharge).</summary>
        private readonly record struct Slot(DateTime Time, double BuyPrice, double SellPrice, double Expected, double MinProduction, double MaxProduction);

        /// <summary>
        /// A product of a market view as a slot: production is minus the target position, which can move from the
        /// held position by up to the buy and sell limits (none for products that cannot be traded any more).
        /// </summary>
        private Slot FromQuote(ProductQuote q)
        {
            double power = PowerMW;
            double min = Math.Max(-q.PositionMW - q.MaxBuyMW, -power);
            double max = Math.Min(-q.PositionMW + q.MaxSellMW, power);
            if (q.MaxBuyMW <= 0 && q.MaxSellMW <= 0) min = max = -q.PositionMW;
            return new Slot(q.Product, q.BuyPrice, q.SellPrice, (q.BuyPrice + q.SellPrice) / 2, min, max);
        }

        /// <summary>
        /// Production per slot: full power in or out where the price clears the window's thresholds and the round
        /// trip pays for the losses, otherwise idle, always within what the battery's energy (followed from its
        /// current content) and the market allow.
        /// </summary>
        private List<(DateTime Time, double Production)> Schedule(List<Slot> slots, List<(DateTime Time, double Price)> expected, double hours)
        {
            double etaC = _battery.ChargeEfficiency.Value, etaD = _battery.DischargeEfficiency.Value;
            double capacity = _battery.NominalEnergyCapacity.CovertToUnit(Units.MegaWattHour).Value;
            double energy = Math.Clamp(EnergyContentMWh, 0, capacity);

            var schedule = new List<(DateTime, double)>(slots.Count);
            foreach (Slot slot in slots)
            {
                var window = expected.Where(p => p.Time >= slot.Time && p.Time < slot.Time + LookaAhead).Select(p => p.Price).ToList();
                double production = 0;
                if (window.Count > 0)
                {
                    double maxPrice = window.Max(), minPrice = window.Min();
                    double buyAt = minPrice + _buyPriceThreshold.ToFraction() * (maxPrice - minPrice);
                    double sellAt = maxPrice - _sellPriceThreshold.ToFraction() * (maxPrice - minPrice);

                    // Buy only what can later be sold above its cost after losses and the wear of both legs, and vice versa.
                    double wear = WearCostPerMWh * (1 + etaC * etaD);
                    if (slot.BuyPrice <= buyAt && slot.BuyPrice < sellAt * etaC * etaD - wear) production = slot.MinProduction;
                    else if (slot.SellPrice > sellAt && slot.SellPrice * etaC * etaD - wear > buyAt) production = slot.MaxProduction;
                }

                // Within the market's range and the battery's energy. Where held positions leave no feasible choice,
                // keep as close to the battery's limits as the market allows (the EMS truncates the rest).
                double low = Math.Max(slot.MinProduction, -(capacity - energy) / (etaC * hours));
                double high = Math.Min(slot.MaxProduction, energy * etaD / hours);
                if (low <= high) production = Math.Clamp(production, low, high);
                else production = slot.MinProduction > high ? slot.MinProduction : slot.MaxProduction;

                energy -= production > 0 ? production * hours / etaD : production * hours * etaC;
                energy = Math.Clamp(energy, 0, capacity);
                schedule.Add((slot.Time, production));
            }
            return schedule;
        }

        private double PowerMW => _battery.InitialCapacityC.CovertToUnit(Units.MegaWatt).Value;

        public void OnNext(BatteryState value) => EnergyContentMWh = value.EnergyContent.CovertToUnit(Units.MegaWattHour).Value;

        public void OnError(Exception error) { }

        public void OnCompleted() { }
    }
}
