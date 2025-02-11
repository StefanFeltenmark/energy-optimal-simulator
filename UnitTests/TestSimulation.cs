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
        public void TestSimulator()
        {

            IBatteryPlanner planner = new RandomBatteryPlanner();
            
            BatterySimulator.BatterySimulator simulator = new BatterySimulator.BatterySimulator(planner);

            simulator.Simulate();


        }
    }
}
