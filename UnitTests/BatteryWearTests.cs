using BatterySimulator;
using BatterySimulator.Interfaces;
using Google.OrTools.LinearSolver;
using Xunit;

namespace UnitTests
{
    public class BatteryWearTests
    {
        private static BatteryWearModel DefaultWear() => new SimulationSettings { BatteryWear = true }.CreateWearModel()!;

        /// <summary>The default map with 1.5 times the cycling wear at empty or full: wear = power x an SoC factor, not convex.</summary>
        private static BatteryWearModel NonConvexWear() => new SimulationSettings { BatteryWear = true, WearExtremeSoCFactor = 1.5 }.CreateWearModel()!;

        [Fact]
        public void IsOffByDefault()
        {
            Assert.Null(new SimulationSettings().CreateWearModel());
        }

        [Fact]
        public void DefaultsCostAboutFiveEuroPerMWhAtHalfCharge()
        {
            // 60 kEUR/MWh replacement over 20 % capacity loss = 300 kEUR per MWh lost; 0.2 / (2 * 6000) MWh lost per MWh.
            var wear = DefaultWear();

            Assert.Equal(300000, wear.CostPerMWhLost, 6);
            Assert.Equal(5, wear.MarginalCostPerMWh(10), 6);
            Assert.Equal(5, wear.MarginalCostPerMWh(10, soc: 1), 6);
            Assert.Equal(7.5, NonConvexWear().MarginalCostPerMWh(10, soc: 1), 6);
        }

        [Fact]
        public void MatchesThePointsAndIsLinearAlongTheGrid()
        {
            var wear = DefaultWear();
            Assert.True(wear.IsGrid);
            foreach (WearPoint p in wear.Points)
            {
                Assert.Equal(p.CapacityLossMWhPerHour, wear.LossRate(p.SoC, p.NetChargeMW), 12);
                Assert.Equal(p.CapacityLossMWhPerHour, wear.EnvelopeLossRate(p.SoC, p.NetChargeMW), 12);
            }

            // At 50 % SoC, no calendar term: the loss is proportional to the power, charging or discharging.
            double full = wear.LossRate(0.5, 10);
            Assert.Equal(full / 2, wear.LossRate(0.5, 5), 12);
            Assert.Equal(full / 2, wear.LossRate(0.5, -5), 12);
            Assert.Equal(0, wear.LossRate(0.5, 0), 12);
        }

        [Fact]
        public void InterpolatesTheMapLikeALookupTable()
        {
            // 25 % SoC, discharging 5 MW: cycling factor halfway between 1.5 and 1, half of 50 EUR/h at full power.
            // POMA's reading mixes half of (50 %, 10 MW) with half of (0 %, idle) and sees 25 EUR/h.
            var wear = NonConvexWear();
            Assert.Equal(1.25 * 25, wear.Cost(0.25, 5, 1), 9);
            Assert.Equal(25, wear.CostPerMWhLost * wear.EnvelopeLossRate(0.25, 5), 9);

            // POMA's reading is one of the mixes, so it never exceeds the interpolation.
            var random = new Random(3);
            for (int i = 0; i < 200; i++)
            {
                double soc = random.NextDouble(), netCharge = -10 + 20 * random.NextDouble();
                Assert.True(wear.EnvelopeLossRate(soc, netCharge) <= wear.LossRate(soc, netCharge) + 1e-15);
            }
        }

        [Fact]
        public void BothReadingsAgreeWhenTheMapIsConvex()
        {
            // The default map has no extra cycling wear at empty or full: wear is a power part plus an SoC part (calendar
            // ageing), convex with kinks on the grid lines, so interpolation and POMA's cheapest mix are the same.
            var wear = DefaultWear();
            var random = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                double soc = random.NextDouble(), netCharge = -10 + 20 * random.NextDouble();
                Assert.Equal(wear.LossRate(soc, netCharge), wear.EnvelopeLossRate(soc, netCharge), 12);
            }
        }

        [Fact]
        public void InterpolationNeedsAFullGrid()
        {
            var points = new List<WearPoint> { new(0, -10, 1), new(0, 10, 1), new(1, -10, 1), new(1, 10, 1), new(0.5, 0, 0) };
            var wear = new BatteryWearModel(points, 1);

            Assert.False(wear.IsGrid);
            Assert.Throws<InvalidOperationException>(() => wear.LossRate(0.5, 0));
            Assert.Equal(0, wear.EnvelopeLossRate(0.5, 0), 12);
        }

        [Fact]
        public void EqualsTheCheapestConvexCombinationAsInPoma()
        {
            // POMA's LP: min sum w_p loss_p with w >= 0, sum w = 1, sum w soc_p = soc, sum w nc_p = nc.
            // An irregular map, so the envelope is not just interpolation on a grid.
            var points = new List<WearPoint>
            {
                new(0, -10, 3), new(0, 10, 2), new(1, -10, 4), new(1, 10, 5),
                new(0.5, 0, 0.1), new(0.2, 4, 0.5), new(0.8, -6, 2.5), new(0.3, -8, 3.5), new(0, 0, 0.4)
            };
            var wear = new BatteryWearModel(points, 1);
            var random = new Random(7);
            for (int i = 0; i < 200; i++)
            {
                double soc = random.NextDouble(), netCharge = -10 + 20 * random.NextDouble();
                Assert.Equal(CheapestCombination(points, soc, netCharge), wear.EnvelopeLossRate(soc, netCharge), 9);
            }
        }

        [Fact]
        public void RejectsAMapThatDoesNotCoverItsRange()
        {
            // Missing the full-SoC, full-charge corner: POMA would be infeasible there.
            var points = new List<WearPoint> { new(0, -10, 1), new(0, 10, 1), new(1, -10, 1), new(0.5, 10, 1) };
            Assert.Throws<ArgumentException>(() => new BatteryWearModel(points, 1));
        }

        [Fact]
        public async Task TheProfitPaysForTheWear()
        {
            // The same planner on the same prices, with and without wear: the cash is the same, and the profit
            // differs by exactly the wear charged, which follows the map along the recorded SoC and net charge.
            var settings = new SimulationSettings { DurationHours = 24, TimeStepSeconds = 900, SleepMilliseconds = 0, WearAwarePlanning = false };
            using var plain = await Run(settings.Clone());
            settings.BatteryWear = true;
            using var worn = await Run(settings);

            var pnl = worn.PnlManager!;
            Assert.True(pnl.AccWearCost > 0);
            Assert.Equal(plain.PnlManager!.AccProfit[plain.End] - pnl.AccWearCost, pnl.AccProfit[worn.End], 6);
            Assert.Equal(pnl.CapacityLostMWh * worn.Wear!.CostPerMWhLost, pnl.AccWearCost, 6);

            var recorder = worn.Recorder;
            TimeSpan step = TimeSpan.FromSeconds(settings.TimeStepSeconds);
            double expected = 0, soc = settings.InitialSoCPercent / 100;
            for (DateTime t = worn.Start + step; t <= worn.End; t += step)
            {
                expected += worn.Wear.Cost(soc, -recorder.NetCharge[t], step.TotalHours);
                soc = recorder.SoC[t];
            }
            Assert.Equal(expected, pnl.AccWearCost, 6);
            Assert.True(pnl.AccEnvelopeWearCost <= pnl.AccWearCost);
        }

        [Fact]
        public async Task KnowingTheWearMakesPriceLevelTradeLess()
        {
            var settings = new SimulationSettings { DurationHours = 48, TimeStepSeconds = 900, SleepMilliseconds = 0, BatteryWear = true };
            using var aware = await Run(settings.Clone());
            settings.WearAwarePlanning = false;
            using var blind = await Run(settings);

            Assert.True(aware.PnlManager!.AccWearCost < blind.PnlManager!.AccWearCost);
        }

        [Fact]
        public async Task PomaReadsTheMapAsItsCheapestMix()
        {
            // POMA's degradation per interval for its own plan equals the map's envelope (EnvelopeLossRate) at POMA's
            // SoC and net charge; the battery's interpolated wear is at least that. This also pins the SoC convention
            // of the points (percent): POMA before its fix (PR #2066) read them
            // as fractions, saw every state as nearly empty and planned at 1 % of the battery's power; against such a
            // service this test fails.
            // Needs the local POMA service; passes without checking anything if it is not running.
            if (!await ExperimentRunner.Experiment.PomaServiceIsUp()) return;

            var planner = new BatteryPomaPlanner.PomaPlanner();
            using var simulator = new BatterySimulator.BatterySimulator(planner);
            var settings = SimulationSettings.Recent();
            settings.DurationHours = 1;
            settings.TimeStepSeconds = 900;
            settings.SleepMilliseconds = 0;
            settings.BatteryWear = true;
            simulator.SetUp(settings);
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            var solution = planner.LastSolution!.Solution.BatterySolution;
            var netCharge = solution.NetCharge[planner.Battery1];
            var soc = solution.SOC[planner.Battery1];
            var degradation = solution.EnergyDegradation[planner.Battery1];
            var times = netCharge.TimePoints().ToList();
            double pomaLoss = 0, ourLoss = 0;
            for (int i = 0; i < times.Count; i++)
            {
                DateTime t = times[i];
                // The last value lasts as long as the one before it.
                TimeSpan length = i + 1 < times.Count ? times[i + 1] - t : t - times[i - 1];
                double expected = simulator.Wear!.EnvelopeLossRate(soc[t] / 100, netCharge[t]) * length.TotalHours;
                Assert.Equal(expected, degradation[t], 12);
                Assert.True(simulator.Wear.LossRate(soc[t] / 100, netCharge[t]) * length.TotalHours >= expected - 1e-15);
                pomaLoss += degradation[t];
                ourLoss += expected;
            }
            Assert.True(pomaLoss > 0, "POMA's plan should wear the battery.");
            Assert.Equal(ourLoss * simulator.Wear!.CostPerMWhLost, solution.TotalDegCost.Value, 6);
        }

        private static async Task<BatterySimulator.BatterySimulator> Run(SimulationSettings settings)
        {
            var simulator = new BatterySimulator.BatterySimulator(new PriceLevelPlanner());
            simulator.SetUp(settings);
            simulator.SimulationEnabled = true;
            await simulator.Simulate();
            return simulator;
        }

        private static double CheapestCombination(IReadOnlyList<WearPoint> points, double soc, double netCharge)
        {
            using Solver solver = Solver.CreateSolver("GLOP")!;
            Constraint sum = solver.MakeConstraint(1, 1), socRow = solver.MakeConstraint(soc, soc), ncRow = solver.MakeConstraint(netCharge, netCharge);
            Objective objective = solver.Objective();
            foreach (WearPoint p in points)
            {
                Variable w = solver.MakeNumVar(0, double.PositiveInfinity, "");
                sum.SetCoefficient(w, 1);
                socRow.SetCoefficient(w, p.SoC);
                ncRow.SetCoefficient(w, p.NetChargeMW);
                objective.SetCoefficient(w, p.CapacityLossMWhPerHour);
            }
            objective.SetMinimization();
            Assert.Equal(Solver.ResultStatus.OPTIMAL, solver.Solve());
            return objective.Value();
        }
    }
}
