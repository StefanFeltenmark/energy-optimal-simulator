using BatterySimulator;
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Xunit;

namespace UnitTests
{
    public class PriceLevelPlannerTests
    {
        private static readonly DateTime Start = new DateTime(2018, 1, 4, 0, 0, 0, DateTimeKind.Local);
        private static readonly TimeSpan Quarter = TimeSpan.FromMinutes(15);

        /// <summary>A planner for the default battery (10 MW, 20 MWh, 95 % / 95 %) with the given energy and quarter-hourly prices.</summary>
        private static PriceLevelPlanner Planner(double energyMWh, params double[] prices)
        {
            using var simulator = new BatterySimulator.BatterySimulator(new PriceLevelPlanner());
            simulator.SetUp(new SimulationSettings { SleepMilliseconds = 0 });
            var forecast = new TimeSeries(true, true) { IsBreakPointSeries = true };
            for (int k = 0; k < prices.Length; k++) forecast.SetValueAt(Start + k * Quarter, prices[k]);
            return new PriceLevelPlanner
            {
                Battery1 = simulator.Battery1,
                EnergyPriceForecast = forecast,
                EnergyContentMWh = energyMWh,
                LookaAhead = TimeSpan.FromHours(6)
            };
        }

        private static double[] Production(PriceLevelPlanner planner, int n) =>
            Enumerable.Range(0, n).Select(k => planner.GetPlannedProduction(Start + k * Quarter).Value).ToArray();

        [Fact]
        public async Task PlansStayWithinTheBatterysEnergy()
        {
            // Cheap for 4 h, expensive for 4 h, cheap again: from 15 MWh it charges only what fits, then sells only
            // what it holds; the planned energy never leaves [0, 20] MWh.
            double[] prices = Enumerable.Repeat(10.0, 16).Concat(Enumerable.Repeat(100.0, 16)).Concat(Enumerable.Repeat(10.0, 16)).ToArray();
            var planner = Planner(15, prices);
            await planner.UpdatePlan(Start, Quarter, 32);

            double energy = 15;
            foreach (double p in Production(planner, 32))
            {
                energy -= p > 0 ? p * 0.25 / 0.95 : p * 0.25 * 0.95;
                Assert.InRange(energy, -1e-9, 20 + 1e-9);
            }
            Assert.Equal(-10, planner.GetPlannedProduction(Start + Quarter).Value, 9);    // charges while there is room
            Assert.Equal(-0.25 / (0.95 * 0.25), planner.GetPlannedProduction(Start + 2 * Quarter).Value, 9);   // the last 0.25 MWh fill it
            Assert.Equal(0, planner.GetPlannedProduction(Start + 3 * Quarter).Value, 9);  // full
            Assert.True(planner.GetPlannedProduction(Start + 16 * Quarter).Value > 0);    // sells once expensive
            Assert.Equal(0, energy, 6);                                                    // and empties it
        }

        [Fact]
        public async Task DoesNotCycleWhenTheSpreadDoesNotPayForTheLosses()
        {
            // 50 vs 52 EUR/MWh: selling 0.95 x 0.95 of a MWh bought at 50 earns 46.9.
            double[] prices = Enumerable.Range(0, 32).Select(k => k % 8 < 4 ? 50.0 : 52.0).ToArray();
            var planner = Planner(10, prices);
            await planner.UpdatePlan(Start, Quarter, 32);
            Assert.All(Production(planner, 32), p => Assert.Equal(0, p, 9));
        }

        [Fact]
        public async Task WithAMarketViewKeepsHeldPositionsAndTradesAtBidAndAsk()
        {
            var planner = Planner(10);
            var quotes = new List<ProductQuote>
            {
                new(Start, 40, 40, 0, 0, 4),                     // past gate closure: bought 4 MW, must be charged
                new(Start + Quarter, 21, 19, 10, 10, 0),         // cheap: ask 21 is below the buy threshold
                new(Start + 2 * Quarter, 101, 99, 10, 10, 0),    // expensive: bid 99 is above the sell threshold
                new(Start + 3 * Quarter, 22, 18, 3, 10, 0),      // cheap, but the book offers only 3 MW
                new(Start + 4 * Quarter, 61, 59, 10, 10, 0),     // last in the view, so a flat window: idle
            };
            planner.SetMarketView(new MarketView(quotes, 60));
            await planner.UpdatePlan(Start, Quarter, quotes.Count);

            Assert.Equal(new[] { -4.0, -10, 10, -3, 0 }, Production(planner, quotes.Count));
        }

        [Fact]
        public async Task DayAheadBidsFollowTheEnergyThroughHeldProducts()
        {
            // Rest of today held: 2 h of full-power charging (5 MWh into a battery at 14 MWh leaves 1 MWh room).
            // Tomorrow is cheap, expensive, cheap: it can only buy the 1 MWh left, then sell what it holds.
            var planner = Planner(14);
            var quotes = new List<ProductQuote>();
            DateTime t = Start;
            for (int k = 0; k < 8; k++, t += Quarter) quotes.Add(new ProductQuote(t, 50, 50, 0, 0, 10 * 5 / (0.95 * 2 * 10)));
            for (int k = 0; k < 8; k++, t += Quarter) quotes.Add(new ProductQuote(t, 10, 10, 10, 10, 0));
            for (int k = 0; k < 8; k++, t += Quarter) quotes.Add(new ProductQuote(t, 100, 100, 10, 10, 0));
            for (int k = 0; k < 8; k++, t += Quarter) quotes.Add(new ProductQuote(t, 10, 10, 10, 10, 0));

            var bid = await planner.PlanDayAhead(Start, new MarketView(quotes, 50));

            Assert.Equal(24, bid.Count);                                                    // only the bid products
            Assert.Equal(1.0 / 0.95, bid.Where(b => b.Value > 0).Sum(b => b.Value) * 0.25, 6);   // buys what fits
            Assert.Equal(-20 * 0.95, bid.Where(b => b.Value < 0).Sum(b => b.Value) * 0.25, 6);   // sells all it holds
        }
    }
}
