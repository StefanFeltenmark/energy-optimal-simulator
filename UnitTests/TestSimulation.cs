using BatterySimulator;
using BatterySimulator.Interfaces;
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
    }
}
