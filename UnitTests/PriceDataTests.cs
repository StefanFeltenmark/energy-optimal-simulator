using BatterySimulator;
using ExperimentRunner;
using Xunit;

namespace UnitTests
{
    public class PriceDataTests
    {
        [Fact]
        public void ReadsTheQuarterHourCsvByLocalClockTime()
        {
            // Around the autumn DST switch: 02:45 occurs twice (+02:00, then +01:00); the second occurrence is kept.
            string file = Path.Combine(Path.GetTempPath(), $"prices_{Guid.NewGuid():N}.csv");
            File.WriteAllText(file,
                "start,SE3,SE3 intraday hourly,SE3 IDA1\n" +
                "2025-10-26T02:30+02:00,40.5,41,\n" +
                "2025-10-26T02:45+02:00,41.25,41,39\n" +
                "2025-10-26T02:00+01:00,38,37,\n" +
                "2025-10-26T02:45+01:00,-1.5,37,\n" +
                "2025-10-26T03:00+01:00,,37,\n");
            try
            {
                var prices = SpotPriceReader.ReadFile(file);

                Assert.Equal(new[] { "SE3" }, prices.Keys);   // intraday and auction columns are not areas
                var se3 = prices["SE3"];
                Assert.Equal(40.5, se3[new DateTime(2025, 10, 26, 2, 30, 0)], 9);
                Assert.Equal(-1.5, se3[new DateTime(2025, 10, 26, 2, 45, 0)], 9);
                Assert.Equal(38, se3[new DateTime(2025, 10, 26, 2, 0, 0)], 9);
                Assert.Equal(3, se3.TimePoints().Count());    // 02:45 once; the empty 03:00 price is skipped
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Theory]
        [InlineData(2025, 10, 23, false)]   // week contains Sunday 26 Oct 2025
        [InlineData(2025, 11, 6, true)]
        [InlineData(2026, 3, 26, false)]    // week contains Sunday 29 Mar 2026
        [InlineData(2018, 3, 22, false)]    // Sunday 25 Mar 2018
        [InlineData(2018, 1, 4, true)]
        public void DaylightSavingSwitchesFollowTheEuRule(int year, int month, int day, bool avoids)
        {
            Assert.Equal(avoids, Experiment.AvoidsDaylightSavingSwitch(new DateTime(year, month, day, 9, 0, 0), 168));
        }

        [Fact]
        public async Task SimulatesADayOnTheRecentQuarterHourData()
        {
            SimulationSettings settings = SimulationSettings.Recent();
            settings.DurationHours = 24;
            settings.TimeStepSeconds = 900;
            settings.SleepMilliseconds = 0;

            using var simulator = new BatterySimulator.BatterySimulator(new PriceLevelPlanner());
            simulator.SetUp(settings);
            simulator.SimulationEnabled = true;
            await simulator.Simulate();

            // Quarter-hours within an hour have their own day-ahead prices.
            DateTime start = simulator.Start;
            Assert.NotEqual(simulator.SellPrices[start], simulator.SellPrices[start.AddMinutes(15)]);

            double profit = simulator.PnlManager!.AccProfit[simulator.End];
            Assert.True(profit <= PerfectForesightBound.ForSimulation(simulator).Profit + 1e-6);
        }
    }
}
