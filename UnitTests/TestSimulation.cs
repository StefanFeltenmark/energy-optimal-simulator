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

        [Fact]
        public async Task SettingsDriveBatteryAndMarket()
        {
            var start = new DateTime(2018, 2, 1, 0, 0, 0, DateTimeKind.Local);
            var settings = new SimulationSettings
            {
                Start = start,
                DurationHours = 2,
                TimeStepSeconds = 900,
                SleepMilliseconds = 0,
                PriceArea = "SE1",
                BuySellSpread = 1.0,
                PowerMW = 5,
                EnergyMWh = 10,
                ChargeEfficiency = 0.9,
                InitialSoCPercent = 20
            };
            // Charge at full power during the first hour only.
            var planner = new FixedPlanner(t => t < start.AddHours(1) ? -5.0 : 0.0);

            using BatterySimulator.BatterySimulator simulator = new BatterySimulator.BatterySimulator(planner);
            simulator.SetUp(settings);
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            // 20 % + 5 MWh * 0.9 into 10 MWh = 65 %.
            Assert.Equal(0.65, simulator.Recorder.SoC[start.AddHours(1)], 6);
            Assert.Equal(TimeSpan.FromSeconds(900), simulator.Delta);

            // 5 MWh bought at the SE1 price for 2018-02-01 00-01 plus the 1 EUR/MWh spread.
            double se1Price = SpotPriceReader.ReadFile(settings.PriceFile)["SE1"][start];
            Assert.Equal(-5 * (se1Price + 1.0), simulator.PnlManager!.AccProfit[start.AddHours(1)], 6);
        }

        [Theory]
        [InlineData(2018, 12, 31, "Prices cover")]
        [InlineData(2017, 12, 31, "Prices cover")]
        public void SetUpRejectsPeriodsOutsideThePriceData(int year, int month, int day, string expected)
        {
            var settings = new SimulationSettings { Start = new DateTime(year, month, day, 12, 0, 0), DurationHours = 24 };
            using var simulator = new BatterySimulator.BatterySimulator(new FixedPlanner(_ => 0));
            var ex = Assert.Throws<ArgumentException>(() => simulator.SetUp(settings));
            Assert.Contains(expected, ex.Message);
        }

        [Fact]
        public void SetUpRejectsInvalidSettings()
        {
            var settings = new SimulationSettings { PriceArea = "XX", SocSoftMinPercent = 90, SocSoftMaxPercent = 10 };
            using var simulator = new BatterySimulator.BatterySimulator(new FixedPlanner(_ => 0));
            var ex = Assert.Throws<ArgumentException>(() => simulator.SetUp(settings));
            Assert.Contains("Soft SoC limits", ex.Message);

            settings.SocSoftMinPercent = 5;
            settings.SocSoftMaxPercent = 95;
            ex = Assert.Throws<ArgumentException>(() => simulator.SetUp(settings));
            Assert.Contains("Price area 'XX' not found", ex.Message);
        }

        [Fact]
        public async Task HorizonAndReplanIntervalChangedMidRunApplyAtNextReplan()
        {
            var calls = new List<(DateTime Start, int Periods)>();
            BatterySimulator.BatterySimulator? sim = null;
            var planner = new FixedPlanner(_ => 0, (planStart, nPeriods) =>
            {
                calls.Add((planStart, nPeriods));
                if (calls.Count == 1)
                {
                    // As if edited in the GUI while the first plan is being made.
                    sim!.Settings.PlanningHorizonHours = 4;
                    sim.Settings.ReplanIntervalMinutes = 60;
                }
            });

            using var simulator = new BatterySimulator.BatterySimulator(planner);
            sim = simulator;
            simulator.SetUp(new SimulationSettings { DurationHours = 3, TimeStepSeconds = 900, SleepMilliseconds = 0 });
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            DateTime start = simulator.Start;
            // Default 8 h horizon (32 x 15 min) first; then 4 h plans every 60 min instead of every 180 min.
            Assert.Equal(new[] { (start, 32), (start.AddHours(1), 16), (start.AddHours(2), 16) }, calls);
        }

        [Fact]
        public async Task PriceLevelThresholdsChangedMidRunApplyAtNextReplan()
        {
            var planner = new PriceLevelPlanner();
            using var simulator = new BatterySimulator.BatterySimulator(planner);
            simulator.SetUp(new SimulationSettings { DurationHours = 4, TimeStepSeconds = 900, SleepMilliseconds = 0 });

            // Change the thresholds one hour in, before the re-plan at 3 h.
            simulator.TimeProvider.PropertyChanged += (_, _) =>
            {
                if (simulator.SimulationTime == simulator.Start.AddHours(1))
                {
                    simulator.Settings.BuyThresholdPercent = 40;
                    simulator.Settings.SellThresholdPercent = 30;
                }
            };
            Assert.Equal(15, planner.BuyPriceThreshold.Value);

            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            Assert.Equal(40, planner.BuyPriceThreshold.Value);
            Assert.Equal(30, planner.SellPriceThreshold.Value);
        }

        [Fact]
        public async Task InvalidMidRunEditsAreIgnored()
        {
            var calls = new List<int>();
            BatterySimulator.BatterySimulator? sim = null;
            var planner = new FixedPlanner(_ => 0, (_, nPeriods) =>
            {
                calls.Add(nPeriods);
                // Re-plan interval longer than the horizon is invalid: keep the previous values.
                if (calls.Count == 1) sim!.Settings.PlanningHorizonHours = 1;
            });

            using var simulator = new BatterySimulator.BatterySimulator(planner);
            sim = simulator;
            simulator.SetUp(new SimulationSettings { DurationHours = 7, TimeStepSeconds = 900, SleepMilliseconds = 0 });
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            Assert.Equal(new[] { 32, 32, 32 }, calls);
        }

        [Fact]
        public async Task PausedRunHoldsItsClockUntilResumedAndCanBeStopped()
        {
            using var simulator = new BatterySimulator.BatterySimulator(new FixedPlanner(_ => 0));
            simulator.SetUp(new SimulationSettings { DurationHours = 2, TimeStepSeconds = 900, SleepMilliseconds = 20 });
            simulator.SimulationEnabled = true;

            Task run = simulator.Simulate();
            simulator.Pause();
            await Task.Delay(300);
            DateTime held = simulator.SimulationTime;
            await Task.Delay(300);
            Assert.Equal(held, simulator.SimulationTime);
            Assert.True(held < simulator.End);

            simulator.Resume();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(simulator.End, simulator.SimulationTime);

            // Stopping a paused run ends it.
            using var cts = new CancellationTokenSource();
            simulator.SetUp(new SimulationSettings { DurationHours = 2, TimeStepSeconds = 900, SleepMilliseconds = 20 });
            simulator.SimulationEnabled = true;
            run = simulator.Simulate(cts.Token);
            simulator.Pause();
            await Task.Delay(100);
            cts.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(simulator.SimulationTime < simulator.End);
        }

        private class FixedPlanner : IBatteryPlanner
        {
            private readonly Func<DateTime, double> _production;
            private readonly Action<DateTime, int>? _onUpdatePlan;

            public FixedPlanner(Func<DateTime, double> production, Action<DateTime, int>? onUpdatePlan = null)
            {
                _production = production;
                _onUpdatePlan = onUpdatePlan;
            }

            public string Name => "FixedPlanner";

            public ObservableTimeSeries PlannedSoC { get; } = new ObservableTimeSeries();

            public Power GetPlannedProduction(DateTime time) => new Power(_production(time), Units.MegaWatt);

            public Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
            {
                _onUpdatePlan?.Invoke(planStart, nPeriods);
                return Task.CompletedTask;
            }
        }
    }
}
