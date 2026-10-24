using BatterySimulator;
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using Xunit;

namespace UnitTests
{
    public class OrderBookTests
    {
        private static readonly DateTime Delivery = new DateTime(2018, 1, 4, 16, 0, 0, DateTimeKind.Local);

        private static TimeSeries Prices(double price = 40)
        {
            var ts = new TimeSeries(true, true);
            for (int h = 0; h < 48; h++) ts.SetValueAt(new DateTime(2018, 1, 4, 0, 0, 0, DateTimeKind.Local).AddHours(h), price + h);
            ts.IsBreakPointSeries = true;
            return ts;
        }

        private static SimulationSettings Settings() => new SimulationSettings { MarketModel = MarketModel.OrderBook };

        [Fact]
        public void MidEndsAtRealizedPriceAndIsIndependentOfQueryOrder()
        {
            var a = new SyntheticOrderBook(Prices(), Settings());
            var b = new SyntheticOrderBook(Prices(), Settings());
            DateTime other = Delivery.AddMinutes(15);

            double aDelivery = a.Mid(Delivery, Delivery.AddHours(-3));
            double aOther = a.Mid(other, Delivery.AddHours(-3));
            double bOther = b.Mid(other, Delivery.AddHours(-3));
            double bDelivery = b.Mid(Delivery, Delivery.AddHours(-3));

            Assert.Equal(aDelivery, bDelivery);
            Assert.Equal(aOther, bOther);
            Assert.NotEqual(Prices()[Delivery], aDelivery);          // it does wander before delivery
            Assert.Equal(Prices()[Delivery], a.Mid(Delivery, Delivery), 9);
        }

        [Fact]
        public void NeighbouringProductsMoveTogether()
        {
            // Without product noise, the market factor moves all mids together: the difference between two
            // products' deviations from their realized prices does not change with trading time.
            var book = new SyntheticOrderBook(Prices(), new SimulationSettings { MarketModel = MarketModel.OrderBook, ProductNoiseVolatility = 0 });
            DateTime next = Delivery.AddMinutes(15);
            double Gap(DateTime t) => (book.Mid(next, t) - Prices()[next]) - (book.Mid(Delivery, t) - Prices()[Delivery]);

            Assert.Equal(Gap(Delivery.AddHours(-8)), Gap(Delivery.AddHours(-1)), 9);
            Assert.NotEqual(book.Mid(Delivery, Delivery.AddHours(-8)), book.Mid(Delivery, Delivery.AddHours(-1)));   // but they do move
        }

        [Fact]
        public void SpreadNarrowsAndDepthGrowsTowardsDelivery()
        {
            var book = new SyntheticOrderBook(Prices(), Settings());
            Assert.True(book.Spread(Delivery, Delivery.AddHours(-12)) > book.Spread(Delivery, Delivery.AddMinutes(-30)));
            Assert.True(book.Asks(Delivery, Delivery.AddHours(-12))[0].VolumeMWh < book.Asks(Delivery, Delivery.AddMinutes(-30))[0].VolumeMWh);
        }

        [Fact]
        public void MarketOrdersWalkTheBookAndFillPartiallyWhenTooLarge()
        {
            var settings = Settings();
            DateTime t = Delivery.AddMinutes(-30);

            Fill small = new SyntheticOrderBook(Prices(), settings).ExecuteMarketOrder(Delivery, 0.5, t);
            Fill large = new SyntheticOrderBook(Prices(), settings).ExecuteMarketOrder(Delivery, 3, t);
            Assert.True(large.AveragePrice > small.AveragePrice);

            var book = new SyntheticOrderBook(Prices(), settings);
            double depth = book.Asks(Delivery, t).Sum(l => l.VolumeMWh);
            Fill tooLarge = book.ExecuteMarketOrder(Delivery, depth + 10, t);
            Assert.Equal(depth, tooLarge.VolumeMWh, 9);

            Fill sell = new SyntheticOrderBook(Prices(), settings).ExecuteMarketOrder(Delivery, -1, t);
            Assert.Equal(-1, sell.VolumeMWh, 9);
            Assert.True(sell.CashEur > 0);
            Assert.True(sell.AveragePrice < small.AveragePrice);
        }

        [Fact]
        public void ConsumedLiquidityRecoversOverTime()
        {
            var book = new SyntheticOrderBook(Prices(), new SimulationSettings { MarketModel = MarketModel.OrderBook, MidVolatility = 0, LiquidityRecoveryMinutes = 10 });
            DateTime t = Delivery.AddHours(-2);

            Fill first = book.ExecuteMarketOrder(Delivery, 1, t);
            Fill immediately = book.ExecuteMarketOrder(Delivery, 1, t);
            Assert.True(immediately.AveragePrice > first.AveragePrice);

            // Much later (many recovery time constants) the best level is back; the spread has also narrowed.
            var fresh = new SyntheticOrderBook(Prices(), new SimulationSettings { MarketModel = MarketModel.OrderBook, MidVolatility = 0, LiquidityRecoveryMinutes = 10 });
            DateTime later = Delivery.AddMinutes(-30);
            Assert.Equal(fresh.ExecuteMarketOrder(Delivery, 1, later).AveragePrice, book.ExecuteMarketOrder(Delivery, 1, later).AveragePrice, 3);
        }

        [Fact]
        public void ExecutorBuysPlannedChargingAndBatteryFollowsThePosition()
        {
            var settings = Settings();
            var executor = new OrderBookExecutor(new SyntheticOrderBook(Prices(), settings), settings, Delivery.AddHours(4));
            var planner = new ConstantPlanner(-8);   // production -8 MW = charging 8 MW
            DateTime t = Delivery.AddHours(-1);

            var (buyCost, sellIncome) = executor.Trade(t, planner, planEnd: Delivery.AddHours(1));

            // Gate closure 30 min: products from t + 30 min up to the plan end are traded, nothing earlier.
            Assert.Equal(0, executor.Position(t.AddMinutes(15)));
            Assert.Equal(2.0, executor.Position(t.AddMinutes(30)), 9);       // 8 MW x 15 min
            Assert.Equal(2.0, executor.Position(Delivery.AddMinutes(45)), 9);
            Assert.Equal(0, executor.Position(Delivery.AddHours(1)));        // beyond the plan
            Assert.Equal(6, executor.Positions.Count);
            Assert.True(buyCost > 0);
            Assert.Equal(0, sellIncome);

            Assert.Equal(8.0, executor.DeliverySetpoint(Delivery.AddMinutes(5)).Value, 9);

            // Trading again with an unchanged plan does nothing.
            Assert.Equal((0.0, 0.0), executor.Trade(t, planner, Delivery.AddHours(1)));
        }

        [Fact]
        public void AverageFillPriceIsVolumeWeightedOverBuysAndSells()
        {
            var settings = Settings();
            var executor = new OrderBookExecutor(new SyntheticOrderBook(Prices(), settings), settings, Delivery.AddHours(4));
            DateTime t = Delivery.AddHours(-1);
            Assert.True(double.IsNaN(executor.AverageFillPrice(Delivery)));

            executor.Trade(t, new ConstantPlanner(-8), Delivery.AddMinutes(15));                 // buy 2 MWh
            executor.Trade(t.AddMinutes(5), new ConstantPlanner(-4), Delivery.AddMinutes(15));   // sell 1 MWh back

            // The same orders on an identical book (each product has its own book state).
            var reference = new SyntheticOrderBook(Prices(), settings);
            Fill buy = reference.ExecuteMarketOrder(Delivery, 2, t);
            Fill sell = reference.ExecuteMarketOrder(Delivery, -1, t.AddMinutes(5));

            Assert.Equal(1, executor.Position(Delivery), 9);
            Assert.Equal((-buy.CashEur + sell.CashEur) / 3, executor.AverageFillPrice(Delivery), 9);
        }

        [Fact]
        public void ExecutorDoesNotTradeBeyondTheRunEnd()
        {
            var settings = Settings();
            var executor = new OrderBookExecutor(new SyntheticOrderBook(Prices(), settings), settings, runEnd: Delivery);
            executor.Trade(Delivery.AddHours(-2), new ConstantPlanner(5), planEnd: Delivery.AddHours(4));
            Assert.All(executor.Positions.Keys, p => Assert.True(p < Delivery));
        }

        [Fact]
        public void ImbalanceIsSettledAtRealizedPriceWithPenalty()
        {
            var settings = Settings();   // penalty 5
            var executor = new OrderBookExecutor(new SyntheticOrderBook(Prices(), settings), settings, Delivery.AddHours(4));
            executor.Trade(Delivery.AddHours(-1), new ConstantPlanner(-8), Delivery.AddHours(1));   // bought 2 MWh per product

            // Battery full: took only 4 MW instead of 8 MW for 15 min -> 1 MWh surplus sold at 40 - 5.
            Assert.Equal((0.0, 1.0 * 35), executor.SettleImbalance(Delivery, Delivery.AddMinutes(15), 4, 40));
            // Took 10 MW -> 0.5 MWh shortfall bought at 40 + 5.
            Assert.Equal((0.5 * 45, 0.0), executor.SettleImbalance(Delivery, Delivery.AddMinutes(15), 10, 40));
            Assert.Equal((0.0, 0.0), executor.SettleImbalance(Delivery, Delivery.AddMinutes(15), 8, 40));
        }

        [Fact]
        public async Task OrderBookConfiguredAsSinglePriceReproducesSinglePriceProfit()
        {
            async Task<double> Run(SimulationSettings s)
            {
                using var sim = new BatterySimulator.BatterySimulator(new PriceLevelPlanner());
                sim.SetUp(s);
                sim.SimulationEnabled = true;
                await sim.Simulate();
                return sim.PnlManager!.AccProfit[sim.Start.AddHours(s.DurationHours)];
            }

            double single = await Run(new SimulationSettings { SleepMilliseconds = 0 });
            double book = await Run(new SimulationSettings
            {
                SleepMilliseconds = 0,
                MarketModel = MarketModel.OrderBook,
                GateClosureMinutes = 0,
                MidVolatility = 0,
                ProductNoiseVolatility = 0,
                SpreadNearDelivery = 0.01,
                SpreadFarFromDelivery = 0.01,
                LevelVolumeMWh = 1e6,
                FarDepthFraction = 1,
                ImbalancePenalty = 0
            });

            Assert.Equal(85.93, single, 2);
            // Same trades; only the bid/ask sits half a spread either side of the price instead of at
            // price / price + spread, and re-planned positions are traded back and forth at that spread.
            Assert.InRange(book - single, -2, 2);
        }

        [Fact]
        public async Task StepCompletedPublishesPositionsPlanAndTotals()
        {
            var snapshots = new List<ExecutionSnapshot>();
            using var sim = new BatterySimulator.BatterySimulator(new ConstantPlanner(-4));   // charge 4 MW
            sim.SetUp(new SimulationSettings { DurationHours = 2, TimeStepSeconds = 900, SleepMilliseconds = 0, MarketModel = MarketModel.OrderBook });
            sim.StepCompleted += (_, s) => snapshots.Add(s);
            sim.SimulationEnabled = true;
            await sim.Simulate();

            Assert.Equal(8, snapshots.Count);                                     // one per 15-minute step
            ExecutionSnapshot first = snapshots[0];
            Assert.True(first.HasOrderBook);
            Assert.Equal(sim.Start, first.IntervalStart);
            Assert.Equal(sim.Start.AddMinutes(15), first.Products[0].Product);    // next product to be delivered
            Assert.Equal(0, first.Products[0].PositionMW);                       // inside gate closure at the start
            Assert.Equal(4, first.Products[2].PositionMW, 9);                    // 09:45, traded at 09:15
            Assert.True(double.IsNaN(first.Products[0].AverageFillPrice));        // never traded
            Assert.Equal(sim.Executor!.AverageFillPrice(first.Products[2].Product), first.Products[2].AverageFillPrice);
            Assert.All(first.Products, p => Assert.Equal(4, p.PlannedMW, 9));

            foreach (ExecutionSnapshot s in snapshots)
            {
                Assert.Equal(sim.Recorder.NetCharge[s.IntervalEnd], s.DeliveredMW);

                // Quotes: all open products from the first after gate closure, 12 h ahead, bid < mid < ask.
                Assert.Equal(sim.Executor!.FirstTradableProduct(s.IntervalEnd), s.Quotes[0].Product);
                Assert.True(s.Quotes[^1].Product < s.IntervalEnd.AddHours(BatterySimulator.BatterySimulator.QuoteHorizonHours));
                Assert.All(s.Quotes, q => Assert.True(q.BestBid < q.Mid && q.Mid < q.BestAsk));
                for (int i = 1; i < s.Quotes.Count; i++) Assert.Equal(s.Quotes[i - 1].Product.AddMinutes(15), s.Quotes[i].Product);
            }
            // At 09:15: products 09:45 (after the 30 min gate) up to 21:00 -> 46 products.
            Assert.Equal(sim.Start.AddMinutes(45), snapshots[0].Quotes[0].Product);
            Assert.Equal(46, snapshots[0].Quotes.Count);
            // Spreads are wider far from delivery than near it.
            Assert.True(snapshots[0].Quotes[^1].BestAsk - snapshots[0].Quotes[^1].BestBid > snapshots[0].Quotes[0].BestAsk - snapshots[0].Quotes[0].BestBid);

            ExecutionSnapshot last = snapshots[^1];
            Assert.Equal(sim.Executor!.GrossBoughtMWh + sim.Executor.GrossSoldMWh, last.GrossTradedMWh);
            Assert.Equal(sim.Executor.TradeCount, last.TradeCount);
        }

        [Fact]
        public async Task StepCompletedHasNoPositionsWithSinglePrice()
        {
            ExecutionSnapshot? snapshot = null;
            using var sim = new BatterySimulator.BatterySimulator(new ConstantPlanner(-4));
            sim.SetUp(new SimulationSettings { DurationHours = 1, TimeStepSeconds = 900, SleepMilliseconds = 0 });
            sim.StepCompleted += (_, s) => snapshot ??= s;
            sim.SimulationEnabled = true;
            await sim.Simulate();

            Assert.NotNull(snapshot);
            Assert.False(snapshot!.HasOrderBook);
            Assert.Empty(snapshot.Quotes);
            Assert.All(snapshot.Products, p => Assert.True(double.IsNaN(p.PositionMW)));
            Assert.All(snapshot.Products, p => Assert.Equal(4, p.PlannedMW, 9));
            Assert.Equal(0, snapshot.TradeCount);
        }

        private class ConstantPlanner : IBatteryPlanner
        {
            private readonly double _productionMW;
            public ConstantPlanner(double productionMW) => _productionMW = productionMW;
            public string Name => "Constant";
            public ObservableTimeSeries PlannedSoC { get; } = new ObservableTimeSeries();
            public Power GetPlannedProduction(DateTime time) => new Power(_productionMW, Units.MegaWatt);
            public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods) => Task.CompletedTask;
        }
    }
}
