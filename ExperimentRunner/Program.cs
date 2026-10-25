using System.Globalization;
using ExperimentRunner;

// Runs planners over many periods and order-book seeds and compares them with the perfect-foresight bound.
//
//   dotnet run --project ExperimentRunner -- --weeks 4 --seeds 5 --planners PriceLevel,POMA --markets single,book
//
// Options (defaults in brackets):
//   --start     first period start, local time [2018-01-04T09:00]
//   --weeks     number of periods, one week apart; periods touching a DST switch are skipped [1]
//   --hours     length of each period [168]
//   --seeds     order-book seeds 1..N; the single-price market is deterministic and runs once [1]
//   --planners  PriceLevel, POMA, Retro, Random [PriceLevel,POMA]
//   --markets   single, book (book-aware planning), naive (order book, reference-price planning) [single]
//   --baseline  planner that paired differences are taken against [first planner]
//   --set       settings overrides for every run, e.g. "MidVolatility=1;ImbalancePenalty=20" []
//   --out       per-run CSV [experiment-results.csv]

var options = ParseOptions(args);
var stdout = Console.Out;
// Leave the current culture alone: the price file's decimal commas are parsed with it.

DateTime firstStart = DateTime.Parse(Get("start", "2018-01-04T09:00"), CultureInfo.InvariantCulture);
int weeks = int.Parse(Get("weeks", "1"));
int hours = int.Parse(Get("hours", "168"));
int seeds = int.Parse(Get("seeds", "1"));
var planners = Get("planners", "PriceLevel,POMA").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
var markets = Get("markets", "single").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(m => Enum.Parse<MarketCase>(m, ignoreCase: true)).ToList();
string baseline = Get("baseline", planners[0]);
string outFile = Get("out", "experiment-results.csv");
var overrides = Get("set", "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
Experiment.ApplyOverrides(new BatterySimulator.SimulationSettings(), overrides);

foreach (string planner in planners) Experiment.CreatePlanner(planner);
if (planners.Any(p => p.Equals("POMA", StringComparison.OrdinalIgnoreCase)) && !await Experiment.PomaServiceIsUp())
{
    stdout.WriteLine("POMA service is not reachable at http://localhost:18262/MultiAsset/health; skipping POMA.");
    planners.RemoveAll(p => p.Equals("POMA", StringComparison.OrdinalIgnoreCase));
}

var starts = new List<DateTime>();
for (int w = 0; w < weeks; w++)
{
    DateTime start = DateTime.SpecifyKind(firstStart.AddDays(7 * w), DateTimeKind.Local);
    if (Experiment.AvoidsDaylightSavingSwitch(start, hours)) starts.Add(start);
    else stdout.WriteLine($"Skipping {start:yyyy-MM-dd HH:mm}: period touches a daylight-saving switch.");
}

var keys = (from start in starts
            from market in markets
            from seed in Enumerable.Range(1, market == MarketCase.Single ? 1 : seeds)
            from planner in planners
            select new RunKey(start, market, seed, planner)).ToList();

// The simulator logs every step to the console; keep only the runner's own output.
Console.SetOut(TextWriter.Null);
var results = new List<RunResult>();
using (var csv = new StreamWriter(outFile))
{
    csv.WriteLine("start,market,seed,planner,profit,bound_free_end,bound_same_end_energy,capture,end_soc_pct,bought_mwh,sold_mwh,gross_traded_mwh,short_imbalance_mwh,long_imbalance_mwh,imbalance_net_cost,runtime_s");
    for (int i = 0; i < keys.Count; i++)
    {
        RunKey key = keys[i];
        RunResult r;
        try
        {
            r = await Experiment.Run(key, hours, overrides);
        }
        catch (Exception e)
        {
            stdout.WriteLine($"[{i + 1}/{keys.Count}] {Describe(key)}: FAILED {e.GetType().Name}: {e.Message}");
            continue;
        }
        results.Add(r);
        csv.WriteLine(string.Join(",",
            key.Start.ToString("yyyy-MM-ddTHH:mm"), key.Market, key.Seed, key.Planner,
            F(r.Profit), F(r.Bound), F(r.BoundSameEndEnergy), F(r.Capture, "F4"), F(r.EndSoCPercent, "F1"),
            F(r.BoughtMWh), F(r.SoldMWh), F(r.GrossTradedMWh), F(r.ShortImbalanceMWh), F(r.LongImbalanceMWh),
            F(r.ImbalanceNetCost), F(r.Runtime.TotalSeconds, "F1")));
        csv.Flush();
        stdout.WriteLine($"[{i + 1}/{keys.Count}] {Describe(key)}: profit {r.Profit:F2}, end SoC {r.EndSoCPercent:F0} %, bound {r.BoundSameEndEnergy:F2} ({r.Capture:P1}) in {r.Runtime.TotalSeconds:F0} s");
    }
}
Console.SetOut(stdout);

PrintSummary(results);
stdout.WriteLine($"Per-run results: {Path.GetFullPath(outFile)}");
return SanityViolations(results) > 0 ? 2 : 0;

void PrintSummary(List<RunResult> results)
{
    stdout.WriteLine();
    stdout.WriteLine($"{"market",-7} {"planner",-11} {"runs",4} {"mean profit",12} {"stdev",9} {"min",9} {"max",9} {"end SoC %",9} {"mean bound",11} {"capture",8} {"free-end bound",15}");
    foreach (var g in results.GroupBy(r => (r.Key.Market, r.Key.Planner)).OrderBy(g => g.Key.Market).ThenBy(g => planners.IndexOf(g.Key.Planner)))
    {
        var profits = g.Select(r => r.Profit).ToList();
        double meanBound = g.Average(r => r.BoundSameEndEnergy);
        stdout.WriteLine($"{g.Key.Market,-7} {g.Key.Planner,-11} {profits.Count,4} {profits.Average(),12:F2} {StDev(profits),9:F2} {profits.Min(),9:F2} {profits.Max(),9:F2} " +
                         $"{g.Average(r => r.EndSoCPercent),9:F1} {meanBound,11:F2} {profits.Average() / meanBound,8:P1} {g.Average(r => r.Bound),15:F2}");
    }

    // Paired differences: same period, market and seed, so the market noise cancels.
    var byKey = results.ToDictionary(r => r.Key);
    var others = planners.Where(p => p != baseline).ToList();
    if (others.Count > 0 && planners.Contains(baseline))
    {
        stdout.WriteLine();
        stdout.WriteLine($"Paired difference vs {baseline} (same period, market and seed):");
        stdout.WriteLine($"{"market",-7} {"planner",-11} {"pairs",5} {"mean diff",10} {"stdev",9} {"std err",8} {"wins",6}");
        foreach (MarketCase market in markets)
        foreach (string planner in others)
        {
            var diffs = results.Where(r => r.Key.Market == market && r.Key.Planner == planner)
                .Select(r => byKey.TryGetValue(r.Key with { Planner = baseline }, out var b) ? r.Profit - b.Profit : double.NaN)
                .Where(d => !double.IsNaN(d)).ToList();
            if (diffs.Count == 0) continue;
            double sd = StDev(diffs);
            stdout.WriteLine($"{market,-7} {planner,-11} {diffs.Count,5} {diffs.Average(),10:F2} {sd,9:F2} {sd / Math.Sqrt(diffs.Count),8:F2} {diffs.Count(d => d > 0),3}/{diffs.Count}");
        }
    }

    SanityViolations(results, print: true);
}

// Each run is compared with perfect foresight for ending with at least the energy it ended with.
// Single price: no strategy can beat that, run by run. Order book: the bound uses realized prices, which bounds
// only the expected profit, and only if the book's mids are fair (martingales); so check the excess over the
// bound averaged over seeds, per period and planner: fail if it is significantly (2 std err) above zero, warn if
// above zero but not significantly, or if there are too few seeds to tell.
int SanityViolations(List<RunResult> results, bool print = false)
{
    const double tolerance = 1e-6;
    const int minSeeds = 5;
    var violations = new List<string>();
    var warnings = new List<string>();
    foreach (var r in results.Where(r => r.Key.Market == MarketCase.Single && r.Profit > r.BoundSameEndEnergy + tolerance))
        violations.Add($"{Describe(r.Key)}: profit {r.Profit:F2} > bound {r.BoundSameEndEnergy:F2}");
    foreach (var g in results.Where(r => r.Key.Market != MarketCase.Single).GroupBy(r => (r.Key.Start, r.Key.Market, r.Key.Planner)))
    {
        var excess = g.Select(r => r.Profit - r.BoundSameEndEnergy).ToList();
        double mean = excess.Average(), stdErr = StDev(excess) / Math.Sqrt(excess.Count);
        if (mean <= tolerance) continue;

        string message = $"{g.Key.Start:yyyy-MM-dd} {g.Key.Market} {g.Key.Planner}: profit exceeds the bound by {mean:F2} " +
                         $"+/- {stdErr:F2} (std err) on average over {excess.Count} seeds ({excess.Count(e => e > 0)} seeds above)";
        if (excess.Count < minSeeds) warnings.Add(message + $"; inconclusive with fewer than {minSeeds} seeds");
        else if (mean > 2 * stdErr) violations.Add(message);
        else warnings.Add(message + "; not significant");
    }

    if (print)
    {
        stdout.WriteLine();
        if (violations.Count == 0) stdout.WriteLine("Sanity check: no planner beats the perfect-foresight bound.");
        else
        {
            stdout.WriteLine($"SANITY CHECK FAILED - profit above the perfect-foresight bound ({violations.Count}); look for information leaks or free money:");
            violations.ForEach(v => stdout.WriteLine("  " + v));
        }
        if (warnings.Count > 0)
        {
            stdout.WriteLine("Warning - order-book profit above the bound on average, but not conclusively:");
            warnings.ForEach(w => stdout.WriteLine("  " + w));
        }
    }
    return violations.Count;
}

static double StDev(IReadOnlyCollection<double> values)
{
    if (values.Count < 2) return 0;
    double mean = values.Average();
    return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
}

static string F(double value, string format = "F2") => value.ToString(format, CultureInfo.InvariantCulture);

static string Describe(RunKey key) => $"{key.Start:yyyy-MM-dd} {key.Market} seed {key.Seed} {key.Planner}";

string Get(string name, string defaultValue) => options.TryGetValue(name, out string? value) ? value : defaultValue;

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--") || i + 1 >= args.Length)
            throw new ArgumentException($"Expected '--name value' pairs, got '{args[i]}'.");
        options[args[i][2..]] = args[++i];
    }
    return options;
}
