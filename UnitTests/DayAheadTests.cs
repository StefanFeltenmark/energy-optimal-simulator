using BatterySimulator;
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using Xunit;

namespace UnitTests
{
    public class DayAheadTests
    {
        private static readonly DateTime Day = new DateTime(2025, 11, 7, 0, 0, 0, DateTimeKind.Local);

        [Fact]
        public void SettlementSetsPositionsAndRejectsSellsAtNegativePrices()
        {
            var settings = new SimulationSettings { MarketModel = MarketModel.OrderBook };
            var prices = new TimeSeries { IsBreakPointSeries = true };
            prices.SetValueAt(Day.AddDays(-2), 50);
            var executor = new OrderBookExecutor(new SyntheticOrderBook(prices, settings), settings, Day.AddDays(1));

            var bid = new Dictionary<DateTime, double>
            {
                [Day] = 8,                      // buy 2 MWh at 40
                [Day.AddMinutes(15)] = -4,      // sell 1 MWh at 60
                [Day.AddMinutes(30)] = -4,      // sell 1 MWh at -3: not accepted (bid from 0 EUR/MWh)
                [Day.AddMinutes(45)] = 4        // buy 1 MWh at -3: accepted, paid -3
            };
            double[] price = { 40, 60, -3, -3 };
            var cash = executor.SettleDayAhead(bid, p => price[(int)((p - Day).TotalMinutes / 15)]);

            Assert.Equal(2, executor.Position(Day), 9);
            Assert.Equal(-1, executor.Position(Day.AddMinutes(15)), 9);
            Assert.Equal(0, executor.Position(Day.AddMinutes(30)), 9);
            Assert.Equal(1, executor.Position(Day.AddMinutes(45)), 9);
            Assert.Equal(2 * 40 - 3, cash.BuyCost, 9);
            Assert.Equal(60, cash.SellIncome, 9);
            Assert.Equal(1, executor.DayAheadRejectedSellMWh, 9);
            Assert.Equal(60 - 77, executor.DayAheadNetCash, 9);
        }

        [Fact]
        public void DayAheadIsForecastUntilPublishedThenTheBookTakesOver()
        {
            var settings = new SimulationSettings { MarketModel = MarketModel.OrderBook, MidVolatility = 0, ProductNoiseVolatility = 0, OpeningCommonStd = 0, OpeningProductStd = 0 };
            var dayAhead = new TimeSeries { IsBreakPointSeries = true };
            dayAhead.SetValueAt(Day.AddDays(-2), 50);
            var forecast = new TimeSeries { IsBreakPointSeries = true };
            forecast.SetValueAt(Day.AddDays(-2), 70);
            var knowledge = new PriceKnowledge(dayAhead, forecast, new SyntheticOrderBook(dayAhead, settings), settings);

            DateTime product = Day.AddHours(10);
            Assert.Equal(70, knowledge.Expected(product, Day.AddHours(-12)));         // 12:00 the day before: forecast
            Assert.Equal(50, knowledge.Expected(product, Day.AddHours(-11)));         // 13:00: published
            Assert.Equal(50, knowledge.Expected(product, Day.AddHours(-9)));          // 15:00: the book's mid (no noise)
            Assert.Equal(50, new PriceKnowledge(dayAhead, null, null, settings).Expected(product, Day.AddHours(-12)));   // no forecast: known all along
        }

        [Fact]
        public async Task BidsSeeOnlyTheForecastAndBecomePositionsAtPublication()
        {
            var settings = SimulationSettings.Recent();
            settings.DurationHours = 30;           // Thu 09:00 to Fri 15:00: a warm-start day and one bid at 12:00
            settings.TimeStepSeconds = 900;
            settings.SleepMilliseconds = 0;
            settings.MarketModel = MarketModel.OrderBook;
            settings.DayAheadMarket = true;
            var forecast = SpotPriceReader.ReadColumn(settings.ForecastFile, "SE3 forecast");

            var spy = new SpyBidder();
            using var simulator = new BatterySimulator.BatterySimulator(spy);
            simulator.SetUp(settings);
            DateTime start = simulator.Start, tomorrow = start.Date.AddDays(1);
            int positionsBefore = 0, positionsAtGate = -1;
            simulator.StepCompleted += (_, s) =>
            {
                // Tomorrow's positions appear when the result is published at 13:00, not at the 12:00 bid: none in the step ending 13:00.
                if (s.IntervalEnd == start.Date.AddHours(13)) positionsAtGate = simulator.Executor!.Positions.Keys.Count(p => p >= tomorrow);
            };
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            Assert.Equal(2, spy.Bids.Count);
            var (warmTime, warmView) = spy.Bids[0];
            var (gateTime, gateView) = spy.Bids[1];

            // Warm start: today's products from the run start, at the forecast (though today's prices are published).
            Assert.Equal(start, warmTime);
            Assert.Equal(start, warmView.Products[0].Product);
            Assert.All(warmView.Products, q => Assert.Equal(forecast[q.Product], q.SellPrice, 9));

            // 12:00: the rest of today is fixed; tomorrow (to the run end) is bid at the forecast, not the day-ahead price.
            Assert.Equal(start.Date.AddHours(12), gateTime);
            var biddable = gateView.Products.Where(q => q.MaxBuyMW > 0).ToList();
            Assert.Equal(tomorrow, biddable[0].Product);
            Assert.Equal(simulator.End.AddMinutes(-15), biddable[^1].Product);
            Assert.All(gateView.Products.Where(q => q.Product < tomorrow), q => Assert.Equal(0, q.MaxBuyMW));
            Assert.All(biddable, q => Assert.Equal(forecast[q.Product], q.SellPrice, 9));
            Assert.Contains(biddable, q => Math.Abs(q.SellPrice - simulator.SellPrices[q.Product]) > 1);

            Assert.Equal(positionsBefore, positionsAtGate);
            Assert.Equal(spy.BidMW * 0.25, simulator.Executor!.Position(tomorrow), 9);
        }

        [Fact]
        public async Task IntradayPlansReachTheLastHeldPosition()
        {
            // With the 8 h horizon alone, the 09:00 plan would end at 17:00 and trade as if the evening's day-ahead
            // purchases did not exist (2025-12-25: positions that overfilled the battery).
            var settings = SimulationSettings.Recent();
            settings.DurationHours = 30;           // Thu 09:00 to Fri 15:00
            settings.TimeStepSeconds = 900;
            settings.SleepMilliseconds = 0;
            settings.MarketModel = MarketModel.OrderBook;
            settings.DayAheadMarket = true;

            var spy = new SpyBidder();
            using var simulator = new BatterySimulator.BatterySimulator(spy);
            simulator.SetUp(settings);
            DateTime start = simulator.Start, tomorrow = start.Date.AddDays(1);
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            // Each plan ends at the later of the 8 h horizon and the last position: before 13:00 today's warm-start
            // purchase at 23:45, from then tomorrow's up to the run end.
            Assert.All(spy.Plans, p =>
            {
                DateTime lastPositionEnd = p.Start < start.Date.AddHours(13) ? tomorrow : simulator.End;
                DateTime horizonEnd = p.Start.AddHours(settings.PlanningHorizonHours);
                Assert.Equal(horizonEnd > lastPositionEnd ? horizonEnd : lastPositionEnd, p.End);
            });
            Assert.Equal(start, spy.Plans[0].Start);
            Assert.Contains(spy.Plans, p => p.Start > start.Date.AddHours(13) && p.End == simulator.End);
        }

        [Fact]
        public async Task SnapshotsShowTomorrowsDayAheadPositionsFromPublication()
        {
            var settings = SimulationSettings.Recent();
            settings.DurationHours = 30;           // Thu 09:00 to Fri 15:00
            settings.TimeStepSeconds = 900;
            settings.SleepMilliseconds = 0;
            settings.MarketModel = MarketModel.OrderBook;
            settings.DayAheadMarket = true;

            using var simulator = new BatterySimulator.BatterySimulator(new SpyBidder());
            simulator.SetUp(settings);
            DateTime start = simulator.Start, tomorrow = start.Date.AddDays(1);
            ExecutionSnapshot? beforePublication = null, afterPublication = null;
            simulator.StepCompleted += (_, s) =>
            {
                if (s.IntervalEnd == start.Date.AddHours(13)) beforePublication = s;
                if (s.IntervalEnd == start.Date.AddHours(13.25)) afterPublication = s;
            };
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            // Before 13:00 the snapshot ends with today's (warm-start) day-ahead positions, at midnight.
            Assert.Equal(tomorrow.AddMinutes(-15), beforePublication!.Products[^1].Product);

            // From 13:00 it covers tomorrow's held products up to the run end, beyond the plan.
            var tomorrows = afterPublication!.Products.Where(p => p.Product >= tomorrow).ToList();
            Assert.Equal(tomorrow, tomorrows[0].Product);
            Assert.Equal(simulator.End.AddMinutes(-15), tomorrows[^1].Product);
            Assert.All(tomorrows, p => Assert.Equal(2, p.DayAheadMW, 9));
            Assert.All(tomorrows, p => Assert.Equal(2, p.PositionMW, 9));
            Assert.All(tomorrows, p => Assert.True(double.IsNaN(p.PlannedMW)));     // beyond the plan
            Assert.All(afterPublication.Products.Where(p => p.Product < start.Date.AddHours(20)), p => Assert.False(double.IsNaN(p.PlannedMW)));
        }

        /// <summary>Records day-ahead requests and bids a constant purchase; does nothing intraday.</summary>
        private class SpyBidder : IBatteryPlanner, IDayAheadBidder
        {
            public readonly double BidMW = 2;
            public List<(DateTime Time, MarketView View)> Bids { get; } = new();
            public string Name => "Spy";
            public ObservableTimeSeries PlannedSoC { get; } = new ObservableTimeSeries();
            public Power GetPlannedProduction(DateTime time) => new Power(-BidMW, Units.MegaWatt);
            public List<(DateTime Start, DateTime End)> Plans { get; } = new();

            public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
            {
                Plans.Add((planStart, planStart + nPeriods * resolution));
                return Task.CompletedTask;
            }

            public Task<IReadOnlyDictionary<DateTime, double>> PlanDayAhead(DateTime planStart, MarketView view)
            {
                Bids.Add((planStart, view));
                IReadOnlyDictionary<DateTime, double> bid = view.Products.Where(q => q.MaxBuyMW > 0).ToDictionary(q => q.Product, _ => BidMW);
                return Task.FromResult(bid);
            }
        }
    }
}
