

using BatterySimulator;

var _planner = new PriceLevelPlanner();
BatterySimulator.BatterySimulator simulator = new BatterySimulator.BatterySimulator(_planner);

simulator.SetUp(2,300);
simulator.SimulationEnabled = true;
simulator.Simulate();

