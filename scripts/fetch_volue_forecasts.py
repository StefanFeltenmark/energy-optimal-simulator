"""Fetch the day-ahead price forecast a trader had at day-ahead gate closure, for every delivery day in a period.

Usage:
    py scripts/fetch_volue_forecasts.py --area se3 --from 2025-10-01 --to 2026-10-25 \
        --out Data/Prices/se3_2025-10-01_2026-10-25_min15_da_forecast.csv

For each delivery day D, takes the latest instance of a Volue Insight 15-minute spot price forecast issued
before the gate closure at 12:00 local time on D-1 (the ECMWF-based "ec00" run is reissued hourly from
06:00; the other runs are fallbacks), and keeps its values for D only. Requires the
`volue-insight-timeseries` package and the OAuth .ini config kept OUTSIDE the repo (--config or the
VOLUE_INSIGHT_CONFIG environment variable).

Output: one row per 15-minute delivery period, "start" in local time with UTC offset as in the price file,
then "SE3 forecast" (EUR/MWh) and "SE3 forecast issued" (the instance's issue time).
"""
import argparse
import os

import pandas as pd
import volue_insight_timeseries as vit

DEFAULT_CONFIG = os.environ.get(
    "VOLUE_INSIGHT_CONFIG",
    r"C:\Users\stefan.feltenmark\Documents\volue_insight.ini",
)
RUNS = ["ec00", "ec12", "gfs06", "gfs00"]
TZ = "Europe/Stockholm"


def main():
    p = argparse.ArgumentParser(description="Fetch day-ahead price forecasts as known at gate closure.")
    p.add_argument("--config", default=DEFAULT_CONFIG, help="Path to the OAuth .ini config.")
    p.add_argument("--area", default="se3")
    p.add_argument("--from", dest="data_from", required=True)
    p.add_argument("--to", dest="data_to", required=True)
    p.add_argument("--gate-closure-hour", type=int, default=12)
    p.add_argument("--out", required=True)
    args = p.parse_args()

    s = vit.Session(config_file=args.config)
    a = args.area.lower()
    curves = [s.get_curve(name=f"pri {a} spot {run} €/mwh cet min15 f") for run in RUNS]

    parts = []
    for day in pd.date_range(args.data_from, args.data_to, freq="D", inclusive="left", tz=TZ):
        # Gate closure on the local calendar: 12:00 on D-1, also when D-1 is 23 or 25 hours long.
        gate = pd.Timestamp(day.date() - pd.Timedelta(days=1)) + pd.Timedelta(hours=args.gate_closure_hour)
        gate = gate.tz_localize(TZ)
        latest_issue = (gate - pd.Timedelta(minutes=1)).isoformat()
        for run, curve in zip(RUNS, curves):
            inst = curve.get_latest(issue_date_to=latest_issue)
            if inst is None:
                continue
            sr = inst.to_pandas()
            # Next local midnight (a calendar day; 23 or 25 hours long at the DST switches).
            sr = sr[(sr.index >= day) & (sr.index < day + pd.DateOffset(days=1))].dropna()
            if len(sr) >= 92:   # a full day (92 or 100 quarter-hours on DST days)
                parts.append(pd.DataFrame({"forecast": sr, "issued": str(inst.issue_date), "run": run}))
                print(f"{day.date()}  {run}  issued {inst.issue_date}", flush=True)
                break
        else:
            print(f"{day.date()}  NO FORECAST issued before {latest_issue}", flush=True)

    df = pd.concat(parts)
    df.index.name = "start"
    out = df.rename(columns={"forecast": f"{a.upper()} forecast", "issued": f"{a.upper()} forecast issued",
                             "run": f"{a.upper()} forecast run"})
    out.index = out.index.strftime("%Y-%m-%dT%H:%M%z").str.replace(r"(\d\d)(\d\d)$", r"\1:\2", regex=True)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    out.round(2).to_csv(args.out)
    print(f"Wrote {len(df)} quarter-hours to {args.out}")


if __name__ == "__main__":
    main()
