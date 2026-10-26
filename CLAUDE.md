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
BatterySimulatorGUI      -->  BatterySimulator        (Avalonia 11 + ReactiveUI + LiveCharts)
BatterySimulator         -->  BatteryPomaPlanner, BatterySimulator.Interfaces, Domain
BatteryPomaPlanner       -->  BatterySimulator.Interfaces, Domain
ExperimentRunner         -->  BatterySimulator        (+ Google.OrTools for the LP)
UnitTests                -->  BatterySimulator, BatteryPomaPlanner, ExperimentRunner
```

### Evaluating strategies (`ExperimentRunner`)
`PerfectForesightBound` is an LP (OR-Tools GLOP) over the same battery model and cash booking as the simulator, on realized prices; a test replays its schedule through the simulator and gets exactly the LP profit. The runner reports each run against the bound **constrained to end with at least the energy the run ended with** — a valid bound for every single-price run, and for the *expected* profit in the order book, where it uses each product's realized intraday price. The runner checks each (week, planner) mean over seeds against it, so a planner beating it in the book points to free money in the market model, not to a good strategy. Compare planners with the printed paired differences (same week, market, seed), not with means. Weeks touching a DST switch are skipped; POMA is skipped if its service is down.

### Price data
`SpotPriceReader.ReadFile` reads either Nord Pool's hourly Elspot export (semicolon-separated, decimal **commas**, parsed with the current culture) or the 15-minute CSV written by `scripts/fetch_volue_prices.py` (detected by its `start,` header; decimal points, invariant culture). In the latter, `start` is local time with UTC offset; columns without a space are day-ahead prices per area, and the intraday columns (hourly index, IDA1–3 auctions) are not read by the simulator; `scripts/calibrate_order_book.py` fits the order book's mid-price model to them. Both readers key prices by local clock time, so the autumn DST hour keeps only its second occurrence and the spring hour is missing — the runner skips weeks touching a DST switch (`Experiment.AvoidsDaylightSavingSwitch`). Re-fetching needs the `volue-insight-timeseries` Python package and the OAuth ini `C:\Users\stefan.feltenmark\Documents\volue_insight.ini` (never commit or print it); the account's intraday history starts in 2020. The runner picks the data with `--data recent|2018`.

### Day-ahead market (`SimulationSettings.DayAheadMarket`, opt-in, order book only)
Every day at `DayAheadGateClosureHour` (12:00) the simulator asks the planner, if it implements `IDayAheadBidder` (POMA, PriceLevel), for the next day's volumes. The `MarketView` it passes holds the rest of today as fixed positions (MaxBuy/MaxSell = 0, no further trading assumed) and tomorrow's products at the **forecast** (`ForecastFile`, column "SE3 forecast": the latest Volue forecast issued before gate closure, from `scripts/fetch_volue_forecasts.py`). Bids are price-independent (buys always accepted; sells bid from 0 EUR/MWh, so rejected when the price clears negative). At `DayAheadPublicationHour` (13:00) `OrderBookExecutor.SettleDayAhead` turns accepted volumes into positions settled at the day-ahead price; intraday trading opens at 15:00 from those positions. Days whose auction closed before the run starts are bid at the start ("warm start", still from the forecast). `PriceKnowledge` is the one rule for what a planner may know about a product's price at time t (forecast → day-ahead price at publication → book mid once open); route anything planner-facing through it. `DayAheadPerfectForecast` is a control that bids on the actual prices. Executor and runner keep day-ahead cash, intraday cash and imbalance apart (profit = day-ahead + intraday − imbalance). `PomaPlanner.PlanDayAhead` reuses its optimisation without touching the intraday plan; `PlanningPeriod` intervals are UTC, so planners keyed by local time must not iterate them (PriceLevel steps through local times).

### Battery wear (`SimulationSettings.BatteryWear`, opt-in)
Wear is POMA's advanced degradation model (`Battery.DegradationMapE`, `UseDegradationModel`, `DegradationCost`): points (SoC, grid-side net charge = discharge − charge, MWh of energy capacity lost per hour), and each MWh lost costs `DegradationCost`. The map can be read two ways between its points (explainer: https://claude.ai/artifact/NTPxZvGpSwmtCKF1eKVQY1). POMA's LP weights the points convexly to reproduce the SoC at the start of an interval and its net charge, and minimises cost, so it reads the **lower convex envelope** of the points (`BatteryWearModel.EnvelopeLossRate`). **The simulator treats the map as a lookup table** (decided 2026-10-26): `LossRate` interpolates bilinearly within the grid cell, which needs the points on a full grid. That is the battery's wear, and POMA is judged against it. The envelope is never above the interpolation, and they agree where the map is convex. `BatteryWearModel` lives in BatterySimulator.Interfaces, so `PomaPlanner` can use it. The simulator charges the interpolation each step, from the SoC before the step and the recorded net charge. `PnLManager.WearCost` / `AccWearCost` / `CapacityLostMWh` hold it, and profit = cash − wear. `AccEnvelopeWearCost` holds the same wear as POMA reads it, so the difference is wear POMA does not see. The LP bound uses the envelope, since an LP cannot hold the interpolation. It stays a valid upper bound, exact for convex maps. The runner adds `wear_cost` / `capacity_lost_mwh` / `wear_cost_poma_reading` columns and an "unseen" column in the summary. `SimulationSettings.CreateWearModel` builds a 3×3 map (SoC 0/50/100 %, net charge −P/0/+P) from a cycle life, end-of-life capacity, replacement cost, an extreme-SoC factor and calendar ageing above 50 %. The defaults are placeholders, not calibrated: about 5 EUR per MWh charged or discharged at 50 % SoC (60 k€/MWh to replace the cells, 6000 cycles to 80 %). 10 EUR/MWh, with the whole system's cost, was judged too high: the plant outlives the cells, batteries run below 80 %, and calendar ageing would end their life anyway. The extreme-SoC factor (`WearExtremeSoCFactor`) defaults to 1, which keeps the default map convex: a power part plus a calendar SoC part, so POMA, the simulator and the bound read it alike. Any other factor multiplies power by an SoC factor, and the map stops being convex. POMA's envelope then mostly drops the factor at part load: it discharges at up to 2·P·SoC at the 50 % cost by mixing in the (0 %, idle) point, so its plans become part-power and tapering. In a week's test this meant 25 % of the time at part power, against 6 % with factor 1. The default was 1.5 until 2026-10-26; keep that value for testing POMA with non-convex maps. `WearAwarePlanning` (default on) decides whether POMA and PriceLevel see the map; PriceLevel uses `MarginalCostPerMWh` at 50 % SoC in its round-trip check. Capacity fade is **not** fed back into the battery: over a week it is around 0.1 % of capacity. First results (2026-10-26, SE3, 4 weeks from 2025-11-06, day-ahead market × 3 seeds, EUR/week; wear per MWh charged or discharged at 50 % SoC):

| Wear | POMA | POMA's wear | POMA cycles/day | PriceLevel | POMA − PriceLevel (paired) |
|---|---|---|---|---|---|
| Off | 12,853 | 0 | 3.3 | 11,442 | +1,411 ± 237 |
| ≈ 2.5 € | 11,022 | 2,004 | 2.2 | 10,071 | +951 ± 317 |
| ≈ 5 € (default) | 9,597 | 3,030 | 1.6 | 8,618 | +979 ± 320 |
| ≈ 5 €, simulator interpolates the map | 9,496 | 3,132 (102 unseen by POMA) | 1.6 | 8,612 | +884 ± 319 |
| ≈ 5 €, convex map (current default) | 10,027 | 2,890 (0 unseen) | 1.8 | 9,115 | +911 ± 327 |
| ≈ 10 € | 7,385 | 4,410 | 1.2 | 5,962 | +1,423 ± 283 |
| ≈ 10 €, planners blind to it | 824 | 12,029 | 3.3 | 5,118 | |

At 10 €, knowing the wear is worth +6,561 ± 168/week to POMA and +844 ± 277 to PriceLevel (paired), and +3,618 ± 413 to POMA in the single-price market. PriceLevel cannot see the calendar term, which is 827 of its 4.4k wear at 10 €; without calendar ageing POMA's lead is about the same, +1,299 ± 286. Present wear results as a range: the profit depends mostly on the wear cost guess. All rows except the last two use the old non-convex map (extreme-SoC factor 1.5) with the simulator charging POMA's reading. With that map interpolated, POMA misses about 3 % of its wear (102 of 3,132 EUR/week), and PriceLevel, which runs at full power, misses 6. The convex default has nothing unseen.

**POMA's SoC convention for degradation points (needs POMA develop ≥ `bc6976803`):** `DegradationPoint.SoC` is in percent, and `PomaPlanner` sends `new Percentage(100 * soc)`. Older POMA read it as a fraction (`E = Σ w · InitialCapacityE · SoC.Value`, in both the MB and the OPL model). It reads every SoC-dependent map along its 0 % row and plans our map at 1 % of the power. That was fixed in POMA issue #2065 / PR #2066 (regression test `TestBatteryDegradationMapSoc`). `BatteryWearTests.PomaExpectsTheWearTheSimulatorCharges` checks, when the local service runs, that POMA's per-interval `EnergyDegradation` equals the simulator's map, and it fails against an unfixed POMA. `PomaPlanner.LastSolution` exposes the latest POMA result for checks like this one. The wear results below were run with the unfixed POMA and fraction points, which is equivalent.

### Synthetic order book prices
With `MarketModel.OrderBook`, the price file is the **day-ahead** price, not the realized one. When trading opens (`TradingOpensHour` on the day before delivery) a product's mid jumps away from it (a part common to the delivery day plus a per-product part), then moves with a market-wide random walk plus the product's own; the product walks are correlated across the day's consecutive quarter-hours (AR(1) chain of their increments), and both walks get more volatile towards delivery (`VolatilityAtDeliveryFactor`, `VolatilityBuildUpHours`). Everything freezes at delivery, and that end value is the product's **realized intraday price** (`SyntheticOrderBook.RealizedPrice`), used for imbalance settlement and the runner's bound. The mid-price defaults are fitted to SE3 2025-10..2026-10 by `scripts/calibrate_order_book.py` (method and limits in its docstring): opening std ≈ 7.8 EUR/MWh, product volatility 2.4 EUR/MWh/√h far from delivery and 4.4× that at delivery, neighbouring quarter-hours 0.986 correlated; the market-wide factor fits to 0. Spread, depth and liquidity recovery are **not** calibrated (no order-book data in the Volue Insight account) and the imbalance penalty is a placeholder. The mids must stay martingales: any term that is visible now and predictably vanishes or reverts by delivery (e.g. noise pinned to zero at delivery while realized prices are flat within the hour) is free money for a storage optimiser, and POMA finds it. `OrderBookTests.MidsAreFairPricesForRealizedPrices` guards this. Because the mids are fair, re-planning more often than the default 180 min does not pay in this model: between re-plans there is only unpredictable noise, so revisions mostly cost spread. With the calibrated defaults (2026-10-26, POMA, day-ahead market, 4 weeks from 2025-11-06 x 3 seeds, paired by week and seed): 15 min was −722 ± 226 EUR/week vs 180 min (−528 ± 210 against the bound for the same end energy, since 15 min ends fuller), trading 1.8x the volume; it removes the imbalance (−390) but loses 1525 in intraday cash. The optional `IntradayTradeThresholdMWh` (default 0) skips revisions smaller than the threshold except for the front product, the last one tradable before its gate closure; at 2 MWh it recovers most of that loss (−246 ± 203, −58 ± 196 same-end-energy) but does not beat 180 min. (Before calibration the same study gave −436 and −150 ± 94.) Spread and depth are still uncalibrated, and these results hinge on them.

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
  - `PriceLevelPlanner` (BatterySimulator) — buy below a low threshold, sell above a high threshold, computed per period from min/max over a rolling look-ahead window, and only when the round trip pays for the losses. It observes the EMS and follows the battery's energy through the plan, so its plans are feasible (no imbalance from planning to charge a full battery); with a market view it keeps held positions and buys at the asks / sells at the bids. **Default planner** in the console + GUI, and the runner's baseline (2026-10-26, 4 weeks x 5 seeds, calibrated book: about 11.7k EUR/week in the book vs 1.0k before these changes; POMA made 12.9k in the day-ahead market).
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
- **GUI packages: LiveCharts is pinned to 2.0.0-rc5 on purpose; don't "upgrade" it.** LiveCharts 2.0.5 and the 2.1.0-dev builds crash the GUI after a minute or a few of simulation with a native access violation (`0xC0000005` in `SkiaApi.sk_font_set_size` / `sk_font_set_edging`, called from `LabelGeometry.PeekPaintInfo` while measuring axis labels); rc5, which the GUI used before, does not (2026-10-25: 2.0.5 crashed after 74 s, rc5 ran 15+ min). The GUI runs on Avalonia 11.3 with `ReactiveUI.Avalonia` 11.4 (the successor of `Avalonia.ReactiveUI`; ignore its unlisted 14.7.1). Avalonia 12 also needs a LiveCharts that targets it (2.0.x fails at the first chart with `MissingFieldException: Avalonia.Input.Gestures.PinchEvent`) and SkiaSharp 3, which no LiveCharts release uses yet. Before upgrading LiveCharts or Avalonia, soak-test: set `BATSIM_SOAK=<delay ms>` (e.g. 50) and `dotnet run --project BatterySimulatorGUI` — the window starts an order-book + day-ahead run by itself — and let it run 10–15 minutes; the crash reproduced in about a minute. A process that is still alive may be sitting in the Windows crash dialog; crash stacks of the GUI are in the Windows Application event log (".NET Runtime").
- **Package audit warnings.** On .NET 10 the build reports NU1903 for a vulnerable *transitive* package, `Microsoft.AspNetCore.SignalR.Common` (via the Volue log client in `Powel.Optimal.MultiAsset.Domain`); it was already there on .NET 8, whose SDK only audited direct references. The GUI project pins patched versions of it and of `Tmds.DBus.Protocol`; the other projects still warn. NU1701 for `Microsoft.AspNet.WebApi.Core` (a .NET Framework package from the same Volue packages) is old and harmless here.
- **The tests load OR-Tools' native DLL**, which fails with "filename or extension is too long" when the repo sits in a very long path; build/test copies from a short path.
- **The `.sln` lives in `BatterySimulator/`**, not the repo root, and references the sibling projects with `..\` paths.
