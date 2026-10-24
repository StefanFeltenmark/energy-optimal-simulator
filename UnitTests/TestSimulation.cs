using BatterySimulator;
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using Xunit;
using Xunit.Abstractions;


namespace UnitTests
{
    public class TestSimulation
    {
        private readonly ITestOutputHelper output;

        public TestSimulation(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public async Task TestSimulator()
        {
            IBatteryPlanner planner = new RandomBatteryPlanner();

            using BatterySimulator.BatterySimulator simulator = new BatterySimulator.BatterySimulator(planner);

            // Short horizon, single one-hour step, no pacing delay.
            simulator.SetUp(1, 3600);
            simulator.SleepTime = TimeSpan.Zero;
            simulator.SimulationEnabled = true;

            await simulator.Simulate();

            // The clock advanced past the start, i.e. the loop actually ran.
            Assert.True(simulator.SimulationTime > simulator.Start);
        }

        [Fact]
        public async Task PlannedSetpointIsAppliedToTheIntervalStartingAtItsTime()
        {
            // Charge 10 MW during the first hour only.
            var planner = new FixedPlanner(t => t < new DateTime(2018, 1, 4, 10, 0, 0, DateTimeKind.Local) ? -10.0 : 0.0);

            using BatterySimulator.BatterySimulator simulator = new BatterySimulator.BatterySimulator(planner);
            simulator.SetUp(2, 900);
            simulator.SleepTime = TimeSpan.Zero;
            simulator.SimulationEnabled = true;

            await simulator.Simulate();

            DateTime start = simulator.Start;
            var soc = simulator.Recorder.SoC;

            // 10 MW for 15 min at 95 % charge efficiency into 20 MWh: +11.875 percentage points.
            Assert.Equal(0.61875, soc[start.AddMinutes(15)], 6);
            // The full first hour is charged, and nothing after it: 50 % + 47.5 points.
            Assert.Equal(0.975, soc[start.AddHours(1)], 6);
            Assert.Equal(0.975, soc[start.AddHours(2)], 6);

            // 10 MWh bought in the 09-10 hour is settled at that hour's buy price (34.97 + 0.01 spread).
            Assert.Equal(-349.8, simulator.PnlManager!.AccProfit[start.AddHours(1)], 6);
        }

        private class FixedPlanner : IBatteryPlanner
        {
            private readonly Func<DateTime, double> _production;

            public FixedPlanner(Func<DateTime, double> production) => _production = production;

            public string Name => "FixedPlanner";

            public ObservableTimeSeries PlannedSoC { get; } = new ObservableTimeSeries();

            public Power GetPlannedProduction(DateTime time) => new Power(_production(time), Units.MegaWatt);

            public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods) => Task.CompletedTask;
        }
    }
}
