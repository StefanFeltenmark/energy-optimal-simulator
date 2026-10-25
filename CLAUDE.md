# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A battery energy-storage **simulator** for the Volue Energy Optimal product family. It runs a battery against an **intraday electricity market in real time**, trying to **maximize trading profit**: each interval a *planner* (trading strategy) decides charge/discharge, the physical battery state advances, and SoC / charge / PnL are recorded and plotted. The point is to **evaluate and compare different trading strategies** — including delegating planning to the real POMA (multi-asset optimization) service.

The clock can run either accelerated (fast replay) or in true real time (`BatterySimulator.IsRealTime`, which additionally waits a full `Delta` per tick to track wall-clock against the live market). Two committed price files stand in for the market feed (see *Price data* below): SE3 2025/26 with 15-minute day-ahead prices (used by the GUI, console and runner) and Nord Pool's 2018 hourly file (the `SimulationSettings` defaults, used by the unit tests).

The whole solution targets **.NET 10** (LTS) with nullable + implicit usings enabled; build output (`bin/`, `obj/`) is ignored.

## Build / run / test

```bash
# Build the whole solution
dotnet build BatterySimulator/BatterySimulator.sln

# Run the console simulation (headless, prints to stdout)
dotnet run --project BatterySimulatorConsole

# Run the Avalonia GUI (live charts of SoC / charge / price / PnL)
dotnet run --project BatterySimulatorGUI

# Compare planners over many weeks / order-book seeds against the perfect-foresight bound
# (options documented at the top of ExperimentRunner/Program.cs; exit code 2 if the sanity check fails)
dotnet run --project ExperimentRunner -- --weeks 4 --seeds 10 --planners PriceLevel,POMA --markets single,book

# Tests (xUnit)
dotnet test UnitTests/UnitTests.csproj
dotnet test --filter FullyQualifiedName~SerializationTests   # one test class
```

Packages restore from the private **GitHub Packages feed `Volue`** (`https://nuget.pkg.github.com/Volue/index.json`) — the `Powel.Optimal.MultiAsset.*` packages (Domain, Infrastructure) live there, not on nuget.org. Restore fails without credentials for that source.

## Key external dependency: `Powel.Optimal.MultiAsset.Domain`

Most domain types are **not defined in this repo** — they come from the `Powel.Optimal.MultiAsset.Domain` package: `Battery`, `EnergyMarket`, `BatteryEnergyProvider`, `MultiAssetData`, `PomaSolutionSet`, `TimeSeries`, and the unit/quantity types (`Power`, `Energy`, `Percentage`, `UnitPrice`, `Units`, `Currencies`). When you need to understand one of these, inspect the package, don't search the repo. Quantities are strongly typed and unit-aware — arithmetic carries units and you convert explicitly with `.CovertToUnit(...)` / `.ToUnit(...)` (note the package's spelling `CovertToUnit`). `ProtocolBuffersConfiguration` (used by the protobuf type model in tests and the formatter) also comes from the package.

## Architecture

Project dependency flow (`-->` = references):

```
BatterySimulatorConsole  -->  BatterySimulator
BatterySimulatorGUI      -->  BatterySimulator        (Avalonia 12 + ReactiveUI + LiveCharts)
BatterySimulator         -->  BatteryPomaPlanner, BatterySimulator.Interfaces, Domain
BatteryPomaPlanner       -->  BatterySimulator.Interfaces, Domain
ExperimentRunner         -->  BatterySimulator        (+ Google.OrTools for the LP)
UnitTests                -->  BatterySimulator, BatteryPomaPlanner, ExperimentRunner
```

### Evaluating strategies (`ExperimentRunner`)
`PerfectForesightBound` is an LP (OR-Tools GLOP) over the same battery model and cash booking as the simulator, on realized prices; a test replays its schedule through the simulator and gets exactly the LP profit. The runner reports each run against the bound **constrained to end with at least the energy the run ended with** — a valid bound for every single-price run, and for the *expected* profit in the order book, where it uses each product's realized intraday price. The runner checks each (week, planner) mean over seeds against it, so a planner beating it in the book points to free money in the market model, not to a good strategy. Compare planners with the printed paired differences (same week, market, seed), not with means. Weeks touching a DST switch are skipped; POMA is skipped if its service is down.

### Price data
`SpotPriceReader.ReadFile` reads either Nord Pool's hourly Elspot export (semicolon-separated, decimal **commas**, parsed with the current culture) or the 15-minute CSV written by `scripts/fetch_volue_prices.py` (detected by its `start,` header; decimal points, invariant culture). In the latter, `start` is local time with UTC offset; columns without a space are day-ahead prices per area, and the intraday columns (hourly index, IDA1–3 auctions) are kept for calibration/comparison but not read by the simulator. Both readers key prices by local clock time, so the autumn DST hour keeps only its second occurrence and the spring hour is missing — the runner skips weeks touching a DST switch (`Experiment.AvoidsDaylightSavingSwitch`). Re-fetching needs the `volue-insight-timeseries` Python package and the OAuth ini `C:\Users\stefan.feltenmark\Documents\volue_insight.ini` (never commit or print it); the account's intraday history starts in 2020. The runner picks the data with `--data recent|2018`.

### Day-ahead market (`SimulationSettings.DayAheadMarket`, opt-in, order book only)
Every day at `DayAheadGateClosureHour` (12:00) the simulator asks the planner, if it implements `IDayAheadBidder` (POMA, PriceLevel), for the next day's volumes. The `MarketView` it passes holds the rest of today as fixed positions (MaxBuy/MaxSell = 0, no further trading assumed) and tomorrow's products at the **forecast** (`ForecastFile`, column "SE3 forecast": the latest Volue forecast issued before gate closure, from `scripts/fetch_volue_forecasts.py`). Bids are price-independent (buys always accepted; sells bid from 0 EUR/MWh, so rejected when the price clears negative). At `DayAheadPublicationHour` (13:00) `OrderBookExecutor.SettleDayAhead` turns accepted volumes into positions settled at the day-ahead price; intraday trading opens at 15:00 from those positions. Days whose auction closed before the run starts are bid at the start ("warm start", still from the forecast). `PriceKnowledge` is the one rule for what a planner may know about a product's price at time t (forecast → day-ahead price at publication → book mid once open); route anything planner-facing through it. `DayAheadPerfectForecast` is a control that bids on the actual prices. Executor and runner keep day-ahead cash, intraday cash and imbalance apart (profit = day-ahead + intraday − imbalance). `PomaPlanner.PlanDayAhead` reuses its optimisation without touching the intraday plan; `PlanningPeriod` intervals are UTC, so planners keyed by local time must not iterate them (PriceLevel steps through local times).

### Synthetic order book prices
With `MarketModel.OrderBook`, the price file is the **day-ahead** price, not the realized one. A product's mid starts there when trading opens (`TradingOpensHour` on the day before delivery) and then moves with a market-wide random walk plus its own; both freeze at delivery, and that end value is the product's **realized intraday price** (`SyntheticOrderBook.RealizedPrice`), used for imbalance settlement and the runner's bound. The mids must stay martingales: any term that is visible now and predictably vanishes or reverts by delivery (e.g. noise pinned to zero at delivery while realized prices are flat within the hour) is free money for a storage optimiser, and POMA finds it. `OrderBookTests.MidsAreFairPricesForRealizedPrices` guards this.

The simulation loop lives in `BatterySimulator/BatterySimulator.cs`. `SetUp(nHours, deltaSeconds)` wires everything; `async Task Simulate(CancellationToken)` runs the clock until the end time, `SimulationEnabled` goes false, or the token is cancelled. Each tick it:
1. asks the planner for planned production (`GetPlannedProduction(t)`), inverts the sign, and applies it as the EMS charge setpoint,
2. advances physical state (`await IBatteryEMS.UpdateState`),
3. paces itself with `await Task.Delay(SleepTime, token)` (250 ms by default), plus `Task.Delay(Delta, token)` in real-time mode,
4. updates PnL against realized market prices,
5. increments the simulated clock.
Re-planning happens on a fixed interval (`replanningInterval`, 180 min) by awaiting `planner.UpdatePlan(...)`. Cancellation surfaces as `OperationCanceledException` from `Task.Delay` and is caught inside `Simulate()`, so it returns normally on stop.

**Sign convention (important):** in the EMS, a *positive* setpoint = charging (energy into battery), *negative* = discharging. Note `Simulate()` negates the planner's planned production before calling `SetChargeLevel`, so planner "production" is discharge-positive while the EMS setpoint is charge-positive. Net charge recorded for PnL is `ChargingGrid - DischargingGrid`.

### The four pluggable pieces (`BatterySimulator.Interfaces`)
- `IBatteryPlanner` — the strategy under test. Implementations:
  - `PriceLevelPlanner` (BatterySimulator) — buy below a low threshold, sell above a high threshold, computed from min/max over a look-ahead window. **Default planner** in the console + GUI.
  - `RetroPlanner`, `RandomBatteryPlanner` (BatterySimulator) — alternative strategies.
  - `PomaPlanner` (BatteryPomaPlanner) — calls the real POMA optimization service over HTTP (see below).
- `IBatteryEMS` — the physical battery model. `BatteryEMS` applies the setpoint, integrates energy over the interval with charge/discharge efficiencies, truncates at empty/full, and pushes new `BatteryState` to observers.
- `IPriceForecaster` — `PriceForecaster` produces a forecast (optionally biased by `a`, `b`) from a price series.
- `ITimeProvider` — `SimulationTimeProvider` is the simulated clock; `Increment()` raises `PropertyChanged` so the GUI follows along.

### Observer / push pattern
`BatteryEMS` is an `IObservable<BatteryState>`. `BatteryDataRecorder` and `PomaPlanner` subscribe to it. The recorder converts each pushed state into a set of `ObservableTimeSeries` (SoC, charge/discharge grid & battery, equivalent cycles). `PnLManager` reads the recorder's net-charge series and accumulates profit. The **GUI never polls** — its `MainWindowViewModel` binds LiveCharts collections to the `CollectionChanged` events of those `ObservableTimeSeries`, so plots update as the simulation pushes data.

### `ObservableTimeSeries` — note the duplicate
There are **two** classes named `ObservableTimeSeries`:
- `Domain/ObservableTimeSeries.cs` — declared in namespace **`BatterySimulator`** (not `Domain`). Wraps a `TimeSeries`, raises `INotifyCollectionChanged` on add/reset, and supports a `MaxItems` sliding window for the GUI. This is the one the recorder/PnL/GUI use.
- `IBatteryPlanner.PlannedSoC` is also typed `ObservableTimeSeries`.

When editing, watch which namespace you're importing — the file path (`Domain/`) does not match the namespace (`BatterySimulator`).

### POMA service integration (`BatteryPomaPlanner`)
`PomaPlanner.UpdatePlan` builds a `MultiAssetData` request and POSTs it to the POMA optimization service via `PomaServiceClient`. Transport is **Protocol Buffers** (`ProtocolBuffersFormatter`, media type `application/x-protobuf`) with `IncludeDateTimeKind = true`. The service endpoint is currently **hard-coded** to `http://localhost:18262/MultiAsset/` in `PomaServiceClient.Run`. The planner subscribes to the EMS to feed the battery's current SoC back as the initial state for each re-optimization. `SerializationTests` exists to guard protobuf round-trip fidelity of `MultiAssetData`.

## Things that will trip you up

- **Hard-coded local paths.** The price files are absolute paths in `SimulationSettings` (`DefaultPriceFile` for 2018, `RecentPriceFile` for SE3 2025/26), and so are the default starts (2018-01-04, and 2025-11-06 in `SimulationSettings.Recent()`). `SerializationTests` writes to `C:\temp\Simulator`.
- **Two time axes: simulator = local clock time, POMA = UTC.** The CSV prices, the price forecast, the simulated clock and the recorder/PnL series are keyed by local (CET) clock time. POMA works in UTC: `PlanningPeriod` always converts its start to UTC (an `Unspecified` start is treated as local), so `PomaPlanner` sends a UTC-keyed copy of the prices (`ToUtc`) and its plan / `PlannedSoC` come back UTC-keyed — look them up with `t.ToUniversalTime()`. `TimeSeries` lookups compare raw clock values and ignore `DateTimeKind`, so mixing the two axes silently shifts everything by an hour (or two in summer) instead of failing. Rule: index local series with local `t`, UTC series with `t.ToUniversalTime()`, and never send local-keyed series to POMA.
- **`Percentage` from a quantity ratio holds the raw fraction.** `Percentage p = energyA / energyB;` gives `p.Value == 0.5` for half — i.e. 0.5 %, not 50 % — whereas `new Percentage(50)` is 50 % (`ToFraction() == 0.5`). Compute the ratio as a `double` and construct `new Percentage(100 * fraction)` (as `BatteryEMS.GetSoC` does). `BatteryDataRecorder.SoC` stores fractions (0–1); the GUI multiplies by 100 for display.
- **Async simulation loop.** `Simulate()` is async end to end — no `Thread.Sleep`; pacing is `Task.Delay` honouring the `CancellationToken`. `BatterySimulatorConsole/Program.cs` uses top-level `await simulator.Simulate()`. The GUI runs it via `Task.Run(() => _simulator.Simulate(token), token)` with a `CancellationTokenSource` that its Stop command cancels, so UI updates from `ObservableTimeSeries` events arrive off the UI thread and are marshalled with `OnUiThread`. Remember to set `SimulationEnabled = true` before calling `Simulate()` or the loop exits immediately.
- **GUI packages: LiveCharts is a development build on purpose.** The GUI runs on Avalonia 12 with `ReactiveUI.Avalonia` (the successor of `Avalonia.ReactiveUI`, which stops at 11.x; ignore its unlisted 14.7.1). LiveCharts 2.0.x is compiled against Avalonia 11 and crashes on 12 when the first chart is created (`MissingFieldException: Avalonia.Input.Gestures.PinchEvent`), so `LiveChartsCore.SkiaSharpView.Avalonia` is pinned to `2.1.0-dev-798`. Don't "upgrade" it to a stable 2.0.x; move to the stable 2.1 once released. When checking the GUI after package changes, start it with `dotnet run` and watch for an exception — a process that is still alive may just be sitting in the Windows crash dialog.
- **Package audit warnings.** On .NET 10 the build reports NU1903 for a vulnerable *transitive* package, `Microsoft.AspNetCore.SignalR.Common` (via the Volue log client in `Powel.Optimal.MultiAsset.Domain`); it was already there on .NET 8, whose SDK only audited direct references. The GUI project pins patched versions of it and of `Tmds.DBus.Protocol`; the other projects still warn. NU1701 for `Microsoft.AspNet.WebApi.Core` (a .NET Framework package from the same Volue packages) is old and harmless here.
- **The tests load OR-Tools' native DLL**, which fails with "filename or extension is too long" when the repo sits in a very long path; build/test copies from a short path.
- **The `.sln` lives in `BatterySimulator/`**, not the repo root, and references the sibling projects with `..\` paths.
