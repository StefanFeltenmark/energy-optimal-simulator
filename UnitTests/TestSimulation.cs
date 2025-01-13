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

            BatterySimulator.BatterySimulator simulator = new BatterySimulator.BatterySimulator();

            simulator.Simulate();


        }
    }
}
