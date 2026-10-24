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
