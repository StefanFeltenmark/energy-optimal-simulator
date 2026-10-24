# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A battery energy-storage **simulator** for the Volue Energy Optimal product family. It runs a battery against an **intraday electricity market in real time**, trying to **maximize trading profit**: each interval a *planner* (trading strategy) decides charge/discharge, the physical battery state advances, and SoC / charge / PnL are recorded and plotted. The point is to **evaluate and compare different trading strategies** — including delegating planning to the real POMA (multi-asset optimization) service.

The clock can run either accelerated (fast replay) or in true real time (`BatterySimulator.IsRealTime`, which additionally waits a full `Delta` per tick to track wall-clock against the live market). The committed 2018 hourly price CSV is the stand-in market feed.

The whole solution targets **.NET 8** with nullable + implicit usings enabled.

## Build / run / test

```bash
# Build the whole solution
dotnet build BatterySimulator/BatterySimulator.sln

# Run the console simulation (headless, prints to stdout)
dotnet run --project BatterySimulatorConsole

# Run the Avalonia GUI (live charts of SoC / charge / price / PnL)
dotnet run --project BatterySimulatorGUI

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
BatterySimulatorGUI      -->  BatterySimulator        (Avalonia + ReactiveUI + LiveCharts)
BatterySimulator         -->  BatteryPomaPlanner, BatterySimulator.Interfaces, Domain
BatteryPomaPlanner       -->  BatterySimulator.Interfaces, Domain
UnitTests                -->  BatterySimulator, BatteryPomaPlanner
```

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

- **Hard-coded local paths.** `BatterySimulator.SetUp` reads prices from an absolute path: `C:\Users\stefan.feltenmark\Documents\energy-optimal-simulator\Data\Prices\elspot-prices_2018_hourly_eur.csv`. The simulation start date is also hard-coded (`2018-01-04`). `SerializationTests` writes to `C:\temp\Simulator`.
- **`Percentage` from a quantity ratio holds the raw fraction.** `Percentage p = energyA / energyB;` gives `p.Value == 0.5` for half — i.e. 0.5 %, not 50 % — whereas `new Percentage(50)` is 50 % (`ToFraction() == 0.5`). Compute the ratio as a `double` and construct `new Percentage(100 * fraction)` (as `BatteryEMS.GetSoC` does). `BatteryDataRecorder.SoC` stores fractions (0–1); the GUI multiplies by 100 for display.
- **Async simulation loop.** `Simulate()` is async end to end — no `Thread.Sleep`; pacing is `Task.Delay` honouring the `CancellationToken`. `BatterySimulatorConsole/Program.cs` uses top-level `await simulator.Simulate()`. The GUI runs it via `Task.Run(() => _simulator.Simulate(token), token)` with a `CancellationTokenSource` that its Stop command cancels, so UI updates from `ObservableTimeSeries` events arrive off the UI thread and are marshalled with `OnUiThread`. Remember to set `SimulationEnabled = true` before calling `Simulate()` or the loop exits immediately.
- **`bin/` and `obj/` are partly committed.** Several `bin/Debug/net8.0/*.deps.json` and `Domain/obj/*` files are tracked in git (the `.gitignore` is inconsistent). Don't be surprised by build-output churn in `git status`; avoid adding more.
- **The `.sln` lives in `BatterySimulator/`**, not the repo root, and references the sibling projects with `..\` paths.
