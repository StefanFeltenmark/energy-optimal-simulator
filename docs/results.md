# Experiment results

Results from `ExperimentRunner`, newest conventions first. All profits are EUR/week on SE3 (`--data recent`). "Paired" differences compare runs with the same week, market and seed; ± is one standard error. Compare planners with the paired differences, not with the means. When you add a study, give its date, the command (or the settings that differ from the defaults), and the commit it ran on.

## POMA vs PriceLevel (2026-10-28)

`--weeks 8 --seeds 5 --planners PriceLevel,POMA --markets single,book,dayahead`, current defaults (wear on, convex map, POMA develop with the degradation-point SoC fix), 8 weeks from 2025-11-06. Run on `bd5977d` plus the three fixes below; the single-price and order-book runs of POMA were not rerun after them, since neither changes those markets for POMA.

| Market | Runs | PriceLevel | POMA | POMA − PriceLevel (paired) | Same, against the same-end-energy bound | POMA wins (bound) |
|---|---|---|---|---|---|---|
| Single price | 8 | 5,177 | 6,010 | +833 ± 278 | +347 ± 111 | 8/8 |
| Order book | 40 | 7,614 | 8,012 | +398 ± 223 | +7 ± 200 | 19/40 |
| Day-ahead + book | 40 | 7,222 | 7,652 | +430 ± 153 | +56 ± 134 | 22/40 |

Most of POMA's raw lead is energy it sells off: it ends the week at 40–50 % SoC, PriceLevel at 65–74 %, and the raw difference counts the energy sold and gives nothing for the energy kept. Against the bound that ends with the same energy, POMA keeps a clear edge with a single price and none in the order-book markets. Wear is similar there (POMA 2.7k, PriceLevel 2.6k in the book and 2.8k in day-ahead + book, EUR/week). Both capture about 55 % of the bound in the book against 83–89 % with a single price; the gap is spread and price noise, which are uncalibrated, so this comparison hinges on them.

Three fixes came out of this study, all before the numbers above:

- **A planner timeout ended the run as if it had completed.** Christmas week (2025-12-25), day-ahead, seed 4: POMA's day-ahead bid at 12:00 hit its 600 s MIP time limit, `PomaServiceClient` gave up after 10 minutes, and `Simulate()` took the `TaskCanceledException` for a stop request (419 EUR for a few hours of trading). Now only the caller's token ends a run quietly; the runner reports anything else as FAILED.
- **Intraday plans ignored held positions beyond the 8 h horizon.** At 09:00 that day POMA traded without knowing about the day-ahead buys from 20:15 on, so at 12:00 the rest of the day held about 23 MWh net charge for a 20 MWh battery holding 1 MWh, with negative prices and no trading left. Only POMA's 1e6 EUR/MWh surplus slack made the bid request feasible (objective about −3 M), and with its charge/discharge binaries the MIP did not close. Plans now reach at least the last held position. The run now takes 41 s and makes 5,231 EUR. The request, written with `--set PomaRequestDirectory=...`, is worth giving the POMA team: it is slow on an instance that needs the slack.
- **PriceLevel unwound held positions where it had no signal.** It aimed an idle tradable slot at zero. Once plans reached the whole of tomorrow, it sold back the day-ahead position at 15:00 and bought much of it back later, through the spread both ways (day-ahead + book: 6,523 with the horizon fix alone). Idle slots now keep their position, which also helps in the plain book (7,476 before), where it unwound its own intraday purchases.

Before the fixes the paired differences against the bound were +88 ± 193 in the book and +196 ± 208 in day-ahead + book (without the Christmas run).

## Battery wear (2026-10-26)

SE3, 4 weeks from 2025-11-06, day-ahead market × 3 seeds. Wear is per MWh charged or discharged at 50 % SoC.

| Wear | POMA | POMA's wear | POMA cycles/day | PriceLevel | POMA − PriceLevel (paired) |
|---|---|---|---|---|---|
| Off | 12,853 | 0 | 3.3 | 11,442 | +1,411 ± 237 |
| ≈ 2.5 € | 11,022 | 2,004 | 2.2 | 10,071 | +951 ± 317 |
| ≈ 5 € (default) | 9,597 | 3,030 | 1.6 | 8,618 | +979 ± 320 |
| ≈ 5 €, simulator interpolates the map | 9,496 | 3,132 (102 unseen by POMA) | 1.6 | 8,612 | +884 ± 319 |
| ≈ 5 €, convex map (current default) | 10,027 | 2,890 (0 unseen) | 1.8 | 9,115 | +911 ± 327 |
| ≈ 10 € | 7,385 | 4,410 | 1.2 | 5,962 | +1,423 ± 283 |
| ≈ 10 €, planners blind to it | 824 | 12,029 | 3.3 | 5,118 | |

At 10 €, knowing the wear is worth +6,561 ± 168/week to POMA and +844 ± 277 to PriceLevel (paired), and +3,618 ± 413 to POMA in the single-price market. PriceLevel cannot see the calendar term, which is 827 of its 4.4k wear at 10 €; without calendar ageing POMA's lead is about the same, +1,299 ± 286. Present wear results as a range: the profit depends mostly on the wear cost guess.

All rows except the last two use the old non-convex map (extreme-SoC factor 1.5) with the simulator charging POMA's reading. With that map interpolated, POMA misses about 3 % of its wear (102 of 3,132 EUR/week), and PriceLevel, which runs at full power, misses 6. The convex default has nothing unseen. These runs used a POMA from before the degradation-point SoC fix (PR #2066) with the points sent as fractions, which is equivalent.

## Re-planning frequency (2026-10-26)

POMA, day-ahead market, calibrated order book, 4 weeks from 2025-11-06 × 3 seeds, paired by week and seed, against the default 180 min:

| Variant | vs 180 min | vs 180 min, against the same-end-energy bound |
|---|---|---|
| 15 min | −722 ± 226 | −528 ± 210 |
| 15 min, `IntradayTradeThresholdMWh=2` | −246 ± 203 | −58 ± 196 |

15 min ends fuller, hence the second column. It trades 1.8× the volume and removes the imbalance (−390) but loses 1,525 in intraday cash. Before the order-book calibration the same study gave −436 and −150 ± 94. Spread and depth are still uncalibrated, and these results hinge on them.

## PriceLevel baseline (2026-10-26)

4 weeks × 5 seeds, calibrated book, after making PriceLevel SoC-feasible and book-aware: about 11.7k EUR/week in the book, against 1.0k before these changes. POMA made 12.9k in the day-ahead market. (No wear in these runs; wear has been on in `SimulationSettings.Recent()` since 2026-10-26.)
