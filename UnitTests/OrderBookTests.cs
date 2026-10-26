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
        public void MidJumpsFromDayAheadAtOpeningEndsAtRealizedAndIsIndependentOfQueryOrder()
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

            // Trading opens at 15:00 the day before; the mid jumps away from the day-ahead price then, wanders and stops at delivery.
            DateTime open = new DateTime(2018, 1, 3, 15, 0, 0, DateTimeKind.Local);
            Assert.Equal(open, a.TradingOpens(Delivery));
            Assert.Equal(Prices()[Delivery], a.Mid(Delivery, open.AddMinutes(-5)), 9);
            Assert.NotEqual(Prices()[Delivery], a.Mid(Delivery, open));
            Assert.NotEqual(Prices()[Delivery], aDelivery);
            Assert.Equal(a.RealizedPrice(Delivery), a.Mid(Delivery, Delivery), 9);
            Assert.Equal(a.RealizedPrice(Delivery), a.Mid(Delivery, Delivery.AddHours(2)), 9);
            Assert.NotEqual(Prices()[Delivery], a.RealizedPrice(Delivery));
        }

        [Fact]
        public void MidsAreFairPricesForRealizedPrices()
        {
            // Over many seeds: a mid 4 h before delivery is an unbiased forecast of the realized price, and the
            // gap between two quarter-hours of the same hour does not predict how that gap changes until delivery.
            // (A gap that reliably closes would be free money for a storage trader.)
            DateTime first = Delivery, second = Delivery.AddMinutes(15), t = Delivery.AddHours(-4);
            var forecastErrors = new List<double>();
            var gaps = new List<double>();
            var gapChanges = new List<double>();
            for (int seed = 1; seed <= 300; seed++)
            {
                var book = new SyntheticOrderBook(Prices(), new SimulationSettings { MarketModel = MarketModel.OrderBook, OrderBookSeed = seed });
                forecastErrors.Add(book.RealizedPrice(first) - book.Mid(first, t));
                double gap = book.Mid(second, t) - book.Mid(first, t);
                gaps.Add(gap);
                gapChanges.Add(book.RealizedPrice(second) - book.RealizedPrice(first) - gap);
            }

            // Forecast error std is about 2.4 x sqrt(54.6) = 17.7 (variance rate integrated with the volatility rise
            // over the last 4 h), so the mean of 300 has a std error of 1.0.
            Assert.InRange(forecastErrors.Average(), -4, 4);

            double gapMean = gaps.Average(), changeMean = gapChanges.Average();
            double slope = gaps.Zip(gapChanges, (g, c) => (g - gapMean) * (c - changeMean)).Sum()
                           / gaps.Sum(g => (g - gapMean) * (g - gapMean));
            Assert.InRange(slope, -0.25, 0.25);   // a gap pinned to close at delivery gives -1
        }

        [Fact]
        public void ProductsAreNotTradedBeforeTradingOpens()
        {
            var prices = new TimeSeries(true, true);
            for (int h = 0; h < 72; h++) prices.SetValueAt(new DateTime(2018, 1, 3, 0, 0, 0, DateTimeKind.Local).AddHours(h), 40);
            prices.IsBreakPointSeries = true;
            var settings = Settings();
            var executor = new OrderBookExecutor(new SyntheticOrderBook(prices, settings), settings, new DateTime(2018, 1, 5, 0, 0, 0, DateTimeKind.Local));
            DateTime midnight = new DateTime(2018, 1, 4, 0, 0, 0, DateTimeKind.Local);

            // At 14:00 only today's products are open; tomorrow's open at 15:00.
            executor.Trade(midnight.AddHours(-10), new ConstantPlanner(-8), midnight.AddHours(2));
            Assert.NotEmpty(executor.Positions);
            Assert.All(executor.Positions.Keys, p => Assert.True(p < midnight));

            executor.Trade(midnight.AddHours(-9), new ConstantPlanner(-8), midnight.AddHours(2));
            Assert.Contains(executor.Positions.Keys, p => p >= midnight);
        }

        [Fact]
        public void NeighbouringProductsMoveTogether()
        {
            // Without product noise, the market factor moves all mids together: the difference between two
            // products' deviations from their day-ahead prices does not change with trading time. (The rise in
            // volatility towards delivery scales each product's moves by its own time to delivery, so it is off here.)
            var book = new SyntheticOrderBook(Prices(), new SimulationSettings
            {
                MarketModel = MarketModel.OrderBook, MidVolatility = 3, ProductNoiseVolatility = 0, VolatilityAtDeliveryFactor = 1
            });
            DateTime next = Delivery.AddMinutes(15);
            double Gap(DateTime t) => (book.Mid(next, t) - Prices()[next]) - (book.Mid(Delivery, t) - Prices()[Delivery]);

            Assert.Equal(Gap(Delivery.AddHours(-8)), Gap(Delivery.AddHours(-1)), 9);
            Assert.NotEqual(book.Mid(Delivery, Delivery.AddHours(-8)), book.Mid(Delivery, Delivery.AddHours(-1)));   // but they do move
        }

        [Fact]
        public void CalibratedMidsHaveTheFittedStructure()
        {
            // Over seeds, with the default (fitted) settings: the jump at opening, neighbouring quarter-hours moving
            // almost together and distant ones less, and the volatility rising towards delivery.
            var settings = new SimulationSettings { MarketModel = MarketModel.OrderBook };
            DateTime open = new DateTime(2018, 1, 3, 15, 0, 0, DateTimeKind.Local);
            DateTime next = Delivery.AddMinutes(15), distant = Delivery.AddHours(-8);
            var opening = new List<double>();
            var moves = new List<(double Product, double Next, double Distant)>();
            var early = new List<double>();
            var late = new List<double>();
            for (int seed = 1; seed <= 400; seed++)
            {
                settings.OrderBookSeed = seed;
                var book = new SyntheticOrderBook(Prices(), settings);
                opening.Add(book.Mid(Delivery, open) - Prices()[Delivery]);
                double Move(DateTime p) => book.RealizedPrice(p) - book.Mid(p, open);
                moves.Add((Move(Delivery), Move(next), Move(distant)));
                early.Add(book.Mid(Delivery, Delivery.AddHours(-19)) - book.Mid(Delivery, Delivery.AddHours(-20)));
                late.Add(book.Mid(Delivery, Delivery) - book.Mid(Delivery, Delivery.AddHours(-1)));
            }

            static double Std(IEnumerable<double> x) { var a = x.ToList(); double m = a.Average(); return Math.Sqrt(a.Sum(v => (v - m) * (v - m)) / (a.Count - 1)); }
            static double Corr(IReadOnlyList<double> x, IReadOnlyList<double> y)
            {
                double mx = x.Average(), my = y.Average();
                return x.Zip(y, (a, b) => (a - mx) * (b - my)).Sum() / Math.Sqrt(x.Sum(a => (a - mx) * (a - mx)) * y.Sum(b => (b - my) * (b - my)));
            }

            // Opening std sqrt(4.1^2 + 6.6^2) = 7.8.
            Assert.InRange(Std(opening), 6.8, 8.8);
            var own = moves.Select(m => m.Product).ToList();
            Assert.InRange(Corr(own, moves.Select(m => m.Next).ToList()), 0.95, 1);                // 0.986
            Assert.InRange(Corr(moves.Select(m => m.Next).ToList(), moves.Select(m => m.Distant).ToList()), 0.2, 0.85);   // 0.986^33 = 0.63 for the common part
            // Volatility in the last hour vs 19-20 h before delivery: about 4 x.
            Assert.InRange(Std(late) / Std(early), 3, 5.5);
            Assert.Equal(settings.VolatilityAtDeliveryFactor, new SyntheticOrderBook(Prices(), settings).VolatilityScale(0), 9);
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
        public void SmallRevisionsWaitForTheFrontProduct()
        {
            var settings = new SimulationSettings { MarketModel = MarketModel.OrderBook, IntradayTradeThresholdMWh = 1, LevelVolumeMWh = 100 };
            var executor = new OrderBookExecutor(new SyntheticOrderBook(Prices(), settings), settings, Delivery.AddHours(4));
            DateTime t = Delivery.AddHours(-2), planEnd = Delivery.AddHours(1);
            DateTime front = executor.FirstTradableProduct(t);

            executor.Trade(t, new ConstantPlanner(-8), planEnd);          // 2 MWh per product: above the threshold
            Assert.All(executor.Positions.Values, v => Assert.Equal(2, v, 9));

            executor.Trade(t, new ConstantPlanner(-10), planEnd);         // +0.5 MWh: only the front product trades
            Assert.Equal(2.5, executor.Position(front), 9);
            Assert.All(executor.Positions.Where(p => p.Key > front), p => Assert.Equal(2, p.Value, 9));

            executor.Trade(t, new ConstantPlanner(-4), planEnd);          // -1 MWh (from 2): at the threshold, all trade
            Assert.All(executor.Positions.Where(p => p.Key > front), p => Assert.Equal(1, p.Value, 9));
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
                OpeningCommonStd = 0,
                OpeningProductStd = 0,
                SpreadNearDelivery = 0.01,
                SpreadFarFromDelivery = 0.01,
                LevelVolumeMWh = 1e6,
                FarDepthFraction = 1,
                ImbalancePenalty = 0
            });

            // A regression value: the planner ends this day full, so the day's cash is negative.
            Assert.Equal(-195.24, single, 2);
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

                // Quotes: the delivery day from the run start; tomorrow's products are not open before 15:00.
                // Bid < mid < ask for tradable products; no bid/ask inside gate closure.
                DateTime firstTradable = sim.Executor!.FirstTradableProduct(s.IntervalEnd);
                Assert.Equal(sim.Start, s.Quotes[0].Product);
                Assert.Equal(sim.Start.Date.AddDays(1).AddMinutes(-15), s.Quotes[^1].Product);
                Assert.All(s.Quotes.Where(q => q.Product >= firstTradable), q => Assert.True(q.BestBid < q.Mid && q.Mid < q.BestAsk));
                Assert.All(s.Quotes.Where(q => q.Product < firstTradable), q => Assert.True(double.IsNaN(q.BestBid) && double.IsNaN(q.BestAsk)));
                for (int i = 1; i < s.Quotes.Count; i++) Assert.Equal(s.Quotes[i - 1].Product.AddMinutes(15), s.Quotes[i].Product);
            }
            // 09:00 to 24:00 -> 60 products; at 09:15 the first tradable one is 09:45 (after the 30 min gate).
            Assert.Equal(60, snapshots[0].Quotes.Count);
            BookQuote front = snapshots[0].Quotes.First(q => !double.IsNaN(q.BestBid));
            Assert.Equal(sim.Start.AddMinutes(45), front.Product);
            // Spreads are wider far from delivery than near it.
            Assert.True(snapshots[0].Quotes[^1].BestAsk - snapshots[0].Quotes[^1].BestBid > front.BestAsk - front.BestBid);

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

        [Fact]
        public void QuoteForVolumeIsReadOnlyAndMatchesExecution()
        {
            var book = new SyntheticOrderBook(Prices(), Settings());
            DateTime t = Delivery.AddHours(-1);

            var first = book.QuoteForVolume(Delivery, t, 2, askSide: true);
            var second = book.QuoteForVolume(Delivery, t, 2, askSide: true);
            Assert.Equal(first, second);                                         // nothing consumed

            Fill fill = new SyntheticOrderBook(Prices(), Settings()).ExecuteMarketOrder(Delivery, 2, t);
            Assert.Equal(fill.AveragePrice, first.AveragePrice, 9);
            Assert.Equal(book.Asks(Delivery, t).Sum(l => l.VolumeMWh), first.DepthMWh, 9);
            Assert.True(book.QuoteForVolume(Delivery, t, 2, askSide: false).AveragePrice < first.AveragePrice);
        }

        [Fact]
        public async Task BookAwarePlannerGetsPositionsPricesAndCapsBeforeEveryPlan()
        {
            var planner = new RecordingAwarePlanner(-8);   // always wants to charge 8 MW
            using var sim = new BatterySimulator.BatterySimulator(planner);
            sim.SetUp(new SimulationSettings
            {
                DurationHours = 3, TimeStepSeconds = 900, SleepMilliseconds = 0, MarketModel = MarketModel.OrderBook,
                PlanningHorizonHours = 2, ReplanIntervalMinutes = 60
            });
            sim.SimulationEnabled = true;
            await sim.Simulate();

            DateTime start = sim.Start;
            Assert.Equal(3, planner.Views.Count);                                // initial plan + re-plans at 1 h and 2 h

            IReadOnlyList<ProductQuote> initial = planner.Views[0]!.Products;
            Assert.Equal(8, initial.Count);                                      // 2 h of 15-minute products
            Assert.Equal(start, initial[0].Product);
            Assert.All(initial, q => Assert.Equal(0, q.PositionMW));
            Assert.All(initial.Take(2), q => Assert.Equal((0.0, 0.0), (q.MaxBuyMW, q.MaxSellMW)));     // inside the 30 min gate
            Assert.All(initial.Skip(2), q => Assert.True(q.MaxBuyMW > 0 && q.MaxSellMW > 0 && q.BuyPrice > q.SellPrice));

            // At the 1 h re-plan, products traded so far carry the 8 MW position.
            IReadOnlyList<ProductQuote> replan = planner.Views[1]!.Products;
            Assert.Equal(start.AddHours(1), replan[0].Product);
            Assert.Equal(8, replan.Single(q => q.Product == start.AddHours(1.5)).PositionMW, 9);
            Assert.Equal(sim.Executor!.Position(start.AddHours(1.5)) / 0.25, replan.Single(q => q.Product == start.AddHours(1.5)).PositionMW, 9);

            // Storage value: the average book mid over the next 24 h as seen at planning time (not the realized prices).
            double expectedMean = Enumerable.Range(0, 96).Select(i => sim.Executor.Book.Mid(start.AddMinutes(15 * i), start)).Average();
            Assert.Equal(expectedMean, planner.Views[0]!.MeanMidNext24h, 9);
            var realized = SpotPriceReader.ReadFile(SimulationSettings.DefaultPriceFile)["SE3"];
            double realizedMean = Enumerable.Range(0, 96).Select(i => realized[start.AddMinutes(15 * i)]).Average();
            Assert.NotEqual(realizedMean, planner.Views[0]!.MeanMidNext24h, 3);
        }

        [Theory]
        [InlineData(MarketModel.SinglePrice, true)]
        [InlineData(MarketModel.OrderBook, false)]
        public async Task PlannerPlansAgainstReferencePriceWithoutBookAwareness(MarketModel model, bool bookAware)
        {
            var planner = new RecordingAwarePlanner(0);
            using var sim = new BatterySimulator.BatterySimulator(planner);
            sim.SetUp(new SimulationSettings { DurationHours = 1, TimeStepSeconds = 900, SleepMilliseconds = 0, MarketModel = model, BookAwarePlanning = bookAware });
            sim.SimulationEnabled = true;
            await sim.Simulate();
            Assert.All(planner.Views, Assert.Null);
        }

        private class RecordingAwarePlanner : IBatteryPlanner, IOrderBookAwarePlanner
        {
            private readonly double _productionMW;
            public RecordingAwarePlanner(double productionMW) => _productionMW = productionMW;
            public List<MarketView?> Views { get; } = new();
            private MarketView? _latest;
            public void SetMarketView(MarketView? view) => _latest = view;
            public string Name => "RecordingAware";
            public ObservableTimeSeries PlannedSoC { get; } = new ObservableTimeSeries();
            public Power GetPlannedProduction(DateTime time) => new Power(_productionMW, Units.MegaWatt);
            public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
            {
                Views.Add(_latest);
                return Task.CompletedTask;
            }
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
