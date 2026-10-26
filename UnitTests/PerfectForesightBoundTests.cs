using BatterySimulator;
using BatterySimulator.Interfaces;
using ExperimentRunner;
using Xunit;

namespace UnitTests
{
    public class PerfectForesightBoundTests
    {
        [Fact]
        public void BuysCheapAndSellsDear()
        {
            // One hour at 10, one at 50; empty 10 MW / 20 MWh battery, lossless.
            var result = PerfectForesightBound.Solve(new[] { 10.0, 50.0 }, new[] { 10.0, 50.0 }, 1, 10, 20, 0, 1, 1);

            Assert.Equal(10 * (50 - 10), result.Profit, 6);
            Assert.Equal(new[] { 10.0, 0.0 }, result.ChargeMW, new ToleranceComparer());
            Assert.Equal(new[] { 0.0, 10.0 }, result.DischargeMW, new ToleranceComparer());
        }

        [Fact]
        public void LossesAndSpreadReduceTheProfit()
        {
            // 10 MWh bought at 10 + 1 spread stores 9 MWh; 8.1 MWh of it reaches the grid when sold at 50.
            var result = PerfectForesightBound.Solve(new[] { 11.0, 51.0 }, new[] { 10.0, 50.0 }, 1, 10, 20, 0, 0.9, 0.9);

            Assert.Equal(8.1 * 50 - 10 * 11, result.Profit, 6);
        }

        [Fact]
        public void LeftoverEnergyIsWorthNothingUnlessItMustBeKept()
        {
            // Falling prices: sell the initial 10 MWh at 50; keeping it means buying it back at 10.
            var free = PerfectForesightBound.Solve(new[] { 50.0, 10.0 }, new[] { 50.0, 10.0 }, 1, 10, 20, 10, 1, 1);
            var keep = PerfectForesightBound.Solve(new[] { 50.0, 10.0 }, new[] { 50.0, 10.0 }, 1, 10, 20, 10, 1, 1, minFinalEnergyMWh: 10);

            Assert.Equal(500, free.Profit, 6);
            Assert.Equal(400, keep.Profit, 6);
        }

        [Fact]
        public void RespectsPowerAndEnergyLimits()
        {
            // Two cheap quarter-hours then two dear ones; a 4 MW / 1 MWh battery fills after one quarter-hour.
            var result = PerfectForesightBound.Solve(new[] { 0.0, 0.0, 100.0, 100.0 }, new[] { 0.0, 0.0, 100.0, 100.0 }, 0.25, 4, 1, 0, 1, 1);

            Assert.Equal(100, result.Profit, 6);
            Assert.All(result.EnergyMWh, e => Assert.InRange(e, -1e-9, 1 + 1e-9));
            Assert.All(result.ChargeMW.Concat(result.DischargeMW), p => Assert.InRange(p, -1e-9, 4 + 1e-9));
        }

        [Theory]
        [InlineData("PriceLevel")]
        [InlineData("Retro")]
        [InlineData("Random")]
        public async Task NoPlannerBeatsPerfectForesightInTheSinglePriceMarket(string plannerName)
        {
            using var simulator = new BatterySimulator.BatterySimulator(CreatePlanner(plannerName));
            simulator.SetUp(new SimulationSettings { DurationHours = 48, TimeStepSeconds = 900, SleepMilliseconds = 0 });
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            double profit = simulator.PnlManager!.AccProfit[simulator.End];
            double bound = PerfectForesightBound.ForSimulation(simulator).Profit;

            Assert.True(bound > 0);
            Assert.True(profit <= bound + 1e-6, $"{plannerName}: profit {profit:F2} > bound {bound:F2}");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TheOptimalScheduleEarnsTheBoundInTheSimulator(bool wear)
        {
            // Replaying the LP's net schedule through the simulator books exactly the LP profit, so the LP
            // and the simulator agree on battery physics, cash and wear. (Simultaneous charge and discharge would
            // be lossy, so the optimum does not use it.) The default wear map is convex, so the simulator's
            // interpolation and the LP's reading of the map agree.
            var settings = new SimulationSettings { DurationHours = 24, TimeStepSeconds = 900, SleepMilliseconds = 0, BatteryWear = wear };
            var (bound, simulator) = await ReplayBound(settings);
            using var _ = simulator;

            Assert.Equal(bound.Profit, simulator.PnlManager!.AccProfit[simulator.End], 3);
            Assert.Equal(bound.WearCost, simulator.PnlManager.AccWearCost, 3);
            if (wear) Assert.True(bound.WearCost > 0);
        }

        [Fact]
        public async Task WithANonConvexWearMapTheBoundStillHolds()
        {
            // With a non-convex map the LP reads wear as POMA does, at most what the simulator charges, so the replayed
            // schedule wears more than the LP counted and earns at most the bound.
            var settings = new SimulationSettings { DurationHours = 24, TimeStepSeconds = 900, SleepMilliseconds = 0, BatteryWear = true, WearExtremeSoCFactor = 1.5 };
            var (bound, simulator) = await ReplayBound(settings);
            using var _ = simulator;
            var pnl = simulator.PnlManager!;

            Assert.Equal(bound.WearCost, pnl.AccEnvelopeWearCost, 3);
            Assert.True(pnl.AccWearCost > bound.WearCost);
            Assert.Equal(bound.Profit - (pnl.AccWearCost - bound.WearCost), pnl.AccProfit[simulator.End], 3);
        }

        /// <summary>Solves the bound for <paramref name="settings"/> and replays its net schedule through a simulator.</summary>
        private static async Task<(BoundResult Bound, BatterySimulator.BatterySimulator Simulator)> ReplayBound(SimulationSettings settings)
        {
            using var probe = new BatterySimulator.BatterySimulator(new PriceLevelPlanner());
            probe.SetUp(settings.Clone());
            BoundResult bound = PerfectForesightBound.ForSimulation(probe);

            DateTime start = probe.Start;
            var planner = new SchedulePlanner(t => bound.DischargeMW[Index(t)] - bound.ChargeMW[Index(t)]);
            int Index(DateTime t) => Math.Min((int)((t - start).TotalSeconds / settings.TimeStepSeconds), bound.ChargeMW.Length - 1);

            var simulator = new BatterySimulator.BatterySimulator(planner);
            simulator.SetUp(settings);
            simulator.SimulationEnabled = true;
            await simulator.Simulate();
            return (bound, simulator);
        }

        [Fact]
        public void WearMakesSmallSpreadsNotWorthTrading()
        {
            // From empty, lossless: charging 10 MWh at 0 % SoC wears 7.5 EUR/MWh (1.5 x 5), discharging them from 50 %
            // 5 EUR/MWh. A 10 EUR/MWh spread does not pay for that; a 70 EUR/MWh spread does.
            var wear = new SimulationSettings { BatteryWear = true, WearExtremeSoCFactor = 1.5, WearCalendarLossAtFullPercentPerYear = 0 }.CreateWearModel();
            var free = PerfectForesightBound.Solve(new[] { 40.0, 50.0 }, new[] { 40.0, 50.0 }, 1, 10, 20, 0, 1, 1);
            var cheap = PerfectForesightBound.Solve(new[] { 40.0, 50.0 }, new[] { 40.0, 50.0 }, 1, 10, 20, 0, 1, 1, wear: wear);
            var dear = PerfectForesightBound.Solve(new[] { 10.0, 80.0 }, new[] { 10.0, 80.0 }, 1, 10, 20, 0, 1, 1, wear: wear);

            Assert.Equal(100, free.Profit, 6);
            Assert.Equal(0, cheap.Profit, 6);
            Assert.Equal(10 * (80 - 10) - 75 - 50, dear.Profit, 6);
            Assert.Equal(125, dear.WearCost, 6);
        }

        private static IBatteryPlanner CreatePlanner(string name) => name switch
        {
            "PriceLevel" => new PriceLevelPlanner(),
            "Retro" => new RetroPlanner(),
            _ => new RandomBatteryPlanner()
        };

        private class SchedulePlanner : IBatteryPlanner
        {
            private readonly Func<DateTime, double> _production;
            public SchedulePlanner(Func<DateTime, double> production) => _production = production;
            public string Name => "Schedule";
            public ObservableTimeSeries PlannedSoC { get; } = new ObservableTimeSeries();
            public Powel.Optimal.MultiAsset.Domain.Quantities.Power GetPlannedProduction(DateTime time) =>
                new(_production(time), Powel.Optimal.MultiAsset.Domain.Quantities.Units.MegaWatt);
            public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods) => Task.CompletedTask;
        }

        private class ToleranceComparer : IEqualityComparer<double>
        {
            public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-6;
            public int GetHashCode(double obj) => 0;
        }
    }
}
