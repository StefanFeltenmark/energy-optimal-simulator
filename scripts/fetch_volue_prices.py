"""Fetch day-ahead and intraday prices for one price area from Volue Insight into a 15-minute CSV.

Usage:
    py scripts/fetch_volue_prices.py --area se3 --from 2025-10-01 --to 2026-10-25 \
        --out Data/Prices/se3_2025-10-01_2026-10-25_min15.csv

Requires the `volue-insight-timeseries` package. Auth comes from an OAuth .ini config kept OUTSIDE the
repo; override its path with --config or the VOLUE_INSIGHT_CONFIG environment variable.

One row per 15-minute delivery period, keyed by its start in local time (CET/CEST) with the UTC offset,
which keeps the doubled hour at the autumn DST switch unambiguous. Columns (EUR/MWh, empty if missing), for
area SE3:
    SE3                  day-ahead price (15-minute products from 2025-10-01; hourly before, repeated per quarter)
    SE3 intraday hourly  intraday price index for the hour, repeated per quarter (realized intraday, for comparison)
    SE3 IDA1..IDA3       15-minute intraday auction prices
The simulator reads the columns without a space as day-ahead prices per area.
"""
import argparse
import os

import pandas as pd
import volue_insight_timeseries as vit

DEFAULT_CONFIG = os.environ.get(
    "VOLUE_INSIGHT_CONFIG",
    r"C:\Users\stefan.feltenmark\Documents\volue_insight.ini",
)


def fetch(session, name, data_from, data_to, tag=None):
    curve = session.get_curve(name=name)
    kwargs = {"data_from": data_from, "data_to": data_to}
    if tag:
        kwargs["tag"] = tag
    return curve.get_data(**kwargs).to_pandas()


def main():
    p = argparse.ArgumentParser(description="Fetch day-ahead and intraday prices into a 15-minute CSV.")
    p.add_argument("--config", default=DEFAULT_CONFIG, help="Path to the OAuth .ini config.")
    p.add_argument("--area", default="se3")
    p.add_argument("--from", dest="data_from", required=True)
    p.add_argument("--to", dest="data_to", required=True)
    p.add_argument("--out", required=True)
    args = p.parse_args()

    s = vit.Session(config_file=args.config)
    a = args.area.lower()

    quarters = pd.date_range(args.data_from, args.data_to, freq="15min", inclusive="left", tz="Europe/Stockholm")
    df = pd.DataFrame(index=quarters)

    # Day-ahead: 15-minute curve where it exists, hourly curve (repeated per quarter) before that.
    da_hourly = fetch(s, f"pri {a} spot €/mwh cet h a", args.data_from, args.data_to)
    df["da"] = da_hourly.reindex(quarters, method="ffill", limit=3)
    try:
        da_q = fetch(s, f"pri {a} spot €/mwh cet min15 a", "2025-10-01", args.data_to)
        da_q = da_q[(da_q.index >= quarters[0]) & (da_q.index <= quarters[-1])].dropna()
        df.loc[da_q.index, "da"] = da_q
    except Exception as e:
        print(f"No 15-minute day-ahead curve ({str(e)[-60:]}); using hourly day-ahead throughout.")

    id_hourly = fetch(s, f"pri {a} intraday €/mwh cet h a", args.data_from, args.data_to)
    df["id_hourly"] = id_hourly.reindex(quarters, method="ffill", limit=3)

    for tag in ["IDA1", "IDA2", "IDA3"]:
        ida = fetch(s, f"pri {a} intraday auction sidc €/mwh cet min15 a", args.data_from, args.data_to, tag=tag)
        df[tag.lower()] = ida.reindex(quarters)

    df.index.name = "start"
    out = df.rename(columns={"da": a.upper(), "id_hourly": f"{a.upper()} intraday hourly",
                             "ida1": f"{a.upper()} IDA1", "ida2": f"{a.upper()} IDA2", "ida3": f"{a.upper()} IDA3"})
    out.index = out.index.strftime("%Y-%m-%dT%H:%M%z").str.replace(r"(\d\d)(\d\d)$", r"\1:\2", regex=True)
    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    out.round(2).to_csv(args.out)

    print(f"Wrote {len(df)} quarter-hours {df.index[0]} .. {df.index[-1]} to {args.out}")
    print("Missing values per column:")
    print(df.isna().sum().to_string())


if __name__ == "__main__":
    main()
