# Experiment results

Results from `ExperimentRunner`, newest conventions first. All profits are EUR/week on SE3 (`--data recent`). "Paired" differences compare runs with the same week, market and seed; ± is one standard error. Compare planners with the paired differences, not with the means. When you add a study, give its date, the command (or the settings that differ from the defaults), and the commit it ran on.

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
