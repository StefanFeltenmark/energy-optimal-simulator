using System.Diagnostics;
using BatteryPomaPlanner;
using BatterySimulator;
using BatterySimulator.Interfaces;

namespace ExperimentRunner
{
    /// <summary>
    /// A market set-up to compare planners in. "single" is the hourly single-price market; "book" the synthetic
    /// order book with book-aware planning; "naive" the order book with planners seeing only reference prices.
    /// </summary>
    public enum MarketCase { Single, Book, Naive }

    public record RunKey(DateTime Start, MarketCase Market, int Seed, string Planner);

    public record RunResult(
        RunKey Key,
        double Profit,
        double Bound,
        double BoundSameEndEnergy,
        double EndSoCPercent,
        double BoughtMWh,
        double SoldMWh,
        double GrossTradedMWh,
        double ShortImbalanceMWh,
        double LongImbalanceMWh,
        double ImbalanceNetCost,
        TimeSpan Runtime)
    {
        /// <summary>
        /// Share captured of the perfect-foresight profit for ending with at least the same energy: a fair
        /// comparison between planners that leave different amounts of energy in the battery.
        /// </summary>
        public double Capture => BoundSameEndEnergy > 1e-9 ? Profit / BoundSameEndEnergy : double.NaN;
    }

    public static class Experiment
    {
        public static readonly string[] PlannerNames = { "PriceLevel", "POMA", "Retro", "Random" };

        public static IBatteryPlanner CreatePlanner(string name) => name.ToLowerInvariant() switch
        {
            "pricelevel" => new PriceLevelPlanner(),
            "poma" => new PomaPlanner(),
            "retro" => new RetroPlanner(),
            "random" => new RandomBatteryPlanner(),
            _ => throw new ArgumentException($"Unknown planner '{name}'. Known: {string.Join(", ", PlannerNames)}.")
        };

        public static SimulationSettings CreateSettings(DateTime start, int hours, MarketCase market, int seed) => new()
        {
            Start = start,
            DurationHours = hours,
            TimeStepSeconds = 900,
            SleepMilliseconds = 0,
            MarketModel = market == MarketCase.Single ? MarketModel.SinglePrice : MarketModel.OrderBook,
            BookAwarePlanning = market == MarketCase.Book,
            OrderBookSeed = seed
        };

        /// <summary>Settings that identify a run (<see cref="RunKey"/>) or are fixed by the runner; not overridable.</summary>
        private static readonly HashSet<string> RunnerControlled = new()
        {
            nameof(SimulationSettings.Start), nameof(SimulationSettings.DurationHours), nameof(SimulationSettings.MarketModel),
            nameof(SimulationSettings.BookAwarePlanning), nameof(SimulationSettings.OrderBookSeed), nameof(SimulationSettings.SleepMilliseconds)
        };

        /// <summary>
        /// Sets <see cref="SimulationSettings"/> properties by name from "Name=Value" pairs, e.g. "MidVolatility=1".
        /// Values use invariant culture (decimal point).
        /// </summary>
        public static void ApplyOverrides(SimulationSettings settings, IEnumerable<string> overrides)
        {
            foreach (string assignment in overrides)
            {
                string[] parts = assignment.Split('=', 2, StringSplitOptions.TrimEntries);
                var property = typeof(SimulationSettings).GetProperty(parts[0]);
                if (parts.Length != 2 || property is not { CanWrite: true })
                    throw new ArgumentException($"Cannot set '{assignment}'; expected Name=Value with a settings property name.");
                if (RunnerControlled.Contains(property.Name))
                    throw new ArgumentException($"'{property.Name}' is set by the runner per run; use --start, --hours, --markets or --seeds instead.");

                object value = property.PropertyType.IsEnum
                    ? Enum.Parse(property.PropertyType, parts[1], ignoreCase: true)
                    : Convert.ChangeType(parts[1], property.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                property.SetValue(settings, value);
            }
        }

        /// <summary>Runs one simulation to the end and measures it against the perfect-foresight bound.</summary>
        public static async Task<RunResult> Run(RunKey key, int hours, IReadOnlyList<string>? overrides = null)
        {
            using var simulator = new BatterySimulator.BatterySimulator(CreatePlanner(key.Planner));
            SimulationSettings settings = CreateSettings(key.Start, hours, key.Market, key.Seed);
            ApplyOverrides(settings, overrides ?? Array.Empty<string>());
            simulator.SetUp(settings);
            simulator.SimulationEnabled = true;

            var stopwatch = Stopwatch.StartNew();
            await simulator.Simulate();
            stopwatch.Stop();

            DateTime start = simulator.Start, end = simulator.End;
            TimeSpan step = TimeSpan.FromSeconds(settings.TimeStepSeconds);
            var recorder = simulator.Recorder;
            double bought = 0, sold = 0;
            for (DateTime t = start + step; t <= end; t += step)
            {
                bought += recorder.ChargingGrid[t] * step.TotalHours;
                sold += -recorder.DischargingGrid[t] * step.TotalHours;
            }

            // Any schedule ending with at least this energy earns at most the constrained LP profit, so this bounds
            // the run itself (single price) and its expected profit (order book). The tolerance keeps the LP
            // feasible when the run ends exactly full.
            double endEnergy = Math.Max(0, recorder.EnergyContent[end] - 1e-6);
            var executor = simulator.Executor;
            return new RunResult(
                key,
                simulator.PnlManager!.AccProfit[end],
                PerfectForesightBound.ForSimulation(simulator).Profit,
                PerfectForesightBound.ForSimulation(simulator, endEnergy).Profit,
                100 * recorder.SoC[end],
                bought,
                sold,
                executor != null ? executor.GrossBoughtMWh + executor.GrossSoldMWh : bought + sold,
                executor?.ShortImbalanceMWh ?? 0,
                executor?.LongImbalanceMWh ?? 0,
                executor?.ImbalanceNetCost ?? 0,
                stopwatch.Elapsed);
        }

        /// <summary>
        /// True if the period, including the planners' look-ahead beyond its end, avoids the 2018 daylight-saving
        /// switches, where the price file has a missing or a duplicated hour on the local clock.
        /// </summary>
        public static bool AvoidsDaylightSavingSwitch(DateTime start, int hours, double lookBeyondHours = 24)
        {
            DateTime end = start.AddHours(hours + lookBeyondHours);
            return !new[] { new DateTime(2018, 3, 25), new DateTime(2018, 10, 28) }
                .Any(day => start < day.AddDays(1) && end > day);
        }

        /// <summary>Checks the local POMA service's health endpoint (Negotiate authentication with the current user).</summary>
        public static async Task<bool> PomaServiceIsUp(string baseAddress = "http://localhost:18262/MultiAsset/")
        {
            try
            {
                using var client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true }) { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await client.GetAsync(baseAddress + "health");
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
