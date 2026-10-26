"""Calibrate the synthetic order book's mid-price model to day-ahead, intraday auction and intraday index prices.

Usage:
    py scripts/calibrate_order_book.py Data/Prices/se3_2025-10-01_2026-10-25_min15.csv

Reads the CSV written by fetch_volue_prices.py and prints the fitted SimulationSettings values.

Model (SyntheticOrderBook), for a product p of delivery day D, trading open from 15:00 on D-1, delivered at T_p:
    mid_p(t) - DA_p = OpeningCommonStd x Z_D + OpeningProductStd x O_p
                      + integral from open to t of s(T_p - u) x (MidVolatility dF(u) + ProductNoiseVolatility dY_p(u))
F is a market-wide standard random walk, Z_D a standard normal per delivery day. The increments of the standard
random walks Y_p form an AR(1) chain over the day's consecutive quarter-hours with correlation
ProductNoiseCorrelation. O_p is standard normal with a share OpeningQuarterShare independent per quarter-hour
and the rest an AR(1) chain with correlation OpeningProductCorrelation. Volatility rises towards delivery:
    s(u)^2 = 1 + (VolatilityAtDeliveryFactor^2 - 1) x exp(-u / VolatilityBuildUpHours).
Every term has mean zero given what is known before it, so the mids stay martingales.

Observations: the three SIDC intraday auctions clear at known times after the opening, IDA1 at 15:00 on D-1
(0 h), IDA2 at 22:00 on D-1 (7 h) and IDA3 at 10:00 on D (19 h, products from 12:00 only). An auction price is
taken as the mid plus auction noise that is independent between auctions. Covariances between auctions remove
that noise:
    cov(IDA1 - DA, IDA2 - DA)              = opening variance
    cov(IDA2 - DA, IDA3 - DA) - opening     = diffusion over 0..7 h
    var(IDA3 - DA) - cov23 - auction noise  = diffusion over 7..19 h
with the auction noise variance estimated as var(IDA1 - DA) - opening variance and assumed the same for IDA3.
Differences between products k quarters apart (common moves cancel there) give the correlation parameters.
The hourly intraday index gives the diffusion from IDA3 to late in the session, var(index) - cov(IDA3, index)
per delivery hour, taking the index as the mid IndexHoursBeforeDelivery before the middle of the hour (it is an
average of trades, which mostly happen close to delivery). Only products from 12:00 (covered by all three
auctions) are used, and values are winsorized at 0.5 % / 99.5 %.

Not identified by these data: bid/ask spread and depth (no order-book data). The near-delivery part of the
volatility profile rests on the index's timing assumption; noise in the index makes it an upper bound.
"""
import argparse

import numpy as np
import pandas as pd
from scipy.optimize import least_squares

LAGS = [1, 2, 4, 8, 16, 32]
IDA2_HOURS, IDA3_HOURS = 7.0, 19.0
FIRST_HOUR = 12                    # IDA3 covers delivery from 12:00
OPEN_TO_MIDNIGHT = 9.0             # trading opens at 15:00 on the day before delivery
INDEX_HOURS_BEFORE_DELIVERY = 1.0  # assumed: the index reflects the mid this long before the middle of the hour


def winsorize(values, p=0.005):
    lo, hi = np.nanquantile(values, [p, 1 - p])
    return np.clip(values, lo, hi)


def load(path):
    df = pd.read_csv(path)
    local = pd.to_datetime(df["start"], utc=True).dt.tz_convert("Europe/Stockholm")
    area = [c for c in df.columns if c != "start" and " " not in c][0]
    df = pd.DataFrame({
        "day": local.dt.date,
        "q": local.dt.hour * 4 + local.dt.minute // 15,
        "da": df[area],
        "idx": df[f"{area} intraday hourly"],
        "a1": df[f"{area} IDA1"], "a2": df[f"{area} IDA2"], "a3": df[f"{area} IDA3"],
    })
    return area, df


def auction_moments(dev):
    """Opening variance, auction-noise variance and diffusion over 0..7 h and 7..19 h, from day x quarter
    matrices of deviations from day-ahead (or of differences of them) for the three auctions."""
    v = [winsorize(dev[c].values).ravel() for c in ("a1", "a2", "a3")]
    ok = ~np.isnan(v[0]) & ~np.isnan(v[1]) & ~np.isnan(v[2])
    c = np.cov(np.vstack([x[ok] for x in v]))
    opening = (c[0, 1] + c[0, 2]) / 2
    noise = max(c[0, 0] - opening, 0.0)
    early = c[1, 2] - opening
    late = c[2, 2] - c[1, 2] - noise
    return {"var": np.diag(c), "cov12": c[0, 1], "cov13": c[0, 2], "cov23": c[1, 2],
            "opening": opening, "noise": noise, "early": early, "late": late, "diffusion": early + late}


def index_moments(df):
    """Per delivery hour from FIRST_HOUR: the diffusion from IDA3 to the index, var(index) - cov(IDA3, index)."""
    pm = df[df.q >= 4 * FIRST_HOUR]
    h = pm.groupby([pm.day, pm.q // 4]).agg(da=("da", "mean"), idx=("idx", "first"), a3=("a3", "mean")).dropna()
    out = {}
    for hour, s in h.groupby(level=1):
        c = np.cov(winsorize((s.a3 - s.da).values), winsorize((s.idx - s.da).values))
        out[hour] = c[1, 1] - c[0, 1]
    return out


def correlation(k, share, phi):
    return (1 - share) * phi ** k


def profile_integral(a, b, delivery, factor, build_up):
    """Integral of s(T - u)^2 over trading time u in [a, b], hours after the opening."""
    return (b - a) + (factor ** 2 - 1) * build_up * (np.exp(-(delivery - b) / build_up) - np.exp(-(delivery - a) / build_up))


def fit(df):
    pm = df[df.q >= 4 * FIRST_HOUR]
    dev = {c: (pm[c] - pm.da).groupby([pm.day, pm.q]).first().unstack() for c in ("a1", "a2", "a3")}
    days = dev["a1"].index.intersection(dev["a2"].index).intersection(dev["a3"].index)
    dev = {c: m.loc[days] for c, m in dev.items()}

    total = auction_moments(dev)
    lagged = {k: auction_moments({c: m - m.shift(k, axis=1) for c, m in dev.items()}) for k in LAGS}
    index = index_moments(df)
    lags = np.array(LAGS, dtype=float)

    # Correlation over products: var(Y_p - Y_p-k) / var(Y_p) = 2 w (1 - phi^k), w = product share of the variance.
    # The volatility profile scales common and product terms alike, so it cancels from these ratios.
    d = np.array([lagged[k]["diffusion"] for k in LAGS]) / total["diffusion"]
    r = least_squares(lambda x: np.log(2 * x[0] * (1 - x[1] ** lags) / d),
                      x0=[0.8, 0.97], bounds=([1e-3, 0], [1, 0.9999]))
    w, phi = r.x

    o = np.array([lagged[k]["opening"] for k in LAGS]) / total["opening"]
    r = least_squares(lambda x: np.log(2 * x[0] * (1 - correlation(lags, x[1], x[2])) / o),
                      x0=[0.7, 0.1, 0.9], bounds=([1e-3, 0, 0], [1, 1, 0.9999]))
    w_open, share_open, phi_open = r.x

    # Volatility profile: base variance rate far from delivery, factor and build-up time, from the auction
    # intervals (averaged over the products from FIRST_HOUR) and the IDA3-to-index interval per hour.
    deliveries = OPEN_TO_MIDNIGHT + FIRST_HOUR + 0.25 * np.arange(4 * (24 - FIRST_HOUR)) + 0.125
    hours = np.array(sorted(index))
    index_obs = np.array([index[h] for h in hours])
    hour_mid = OPEN_TO_MIDNIGHT + hours + 0.5

    def model(x):
        base, factor, build_up = x
        early = base * profile_integral(0, IDA2_HOURS, deliveries, factor, build_up).mean()
        late = base * profile_integral(IDA2_HOURS, IDA3_HOURS, deliveries, factor, build_up).mean()
        idx = base * profile_integral(IDA3_HOURS, hour_mid - INDEX_HOURS_BEFORE_DELIVERY, hour_mid, factor, build_up)
        return early, late, idx

    def residuals(x):
        early, late, idx = model(x)
        # The two auction intervals cover every product; weight them like the 12 index hours together.
        return np.concatenate([np.sqrt(6) * np.log([early / total["early"], late / total["late"]]), np.log(idx / index_obs)])

    r = least_squares(residuals, x0=[8, 2, 3], bounds=([1e-3, 1, 0.1], [1e3, 20, 48]))
    base, factor, build_up = r.x

    params = {
        "MidVolatility": np.sqrt((1 - w) * base),
        "ProductNoiseVolatility": np.sqrt(w * base),
        "ProductNoiseCorrelation": phi,
        "VolatilityAtDeliveryFactor": factor,
        "VolatilityBuildUpHours": build_up,
        "OpeningCommonStd": np.sqrt((1 - w_open) * total["opening"]),
        "OpeningProductStd": np.sqrt(w_open * total["opening"]),
        "OpeningProductCorrelation": phi_open,
        "OpeningQuarterShare": share_open,
    }
    fitted = {"d": (d, 2 * w * (1 - phi ** lags)), "o": (o, 2 * w_open * (1 - correlation(lags, share_open, phi_open))),
              "profile": (total, model(r.x), hours, index_obs)}
    return params, total, fitted


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("csv")
    args = p.parse_args()

    area, df = load(args.csv)
    params, total, fitted = fit(df)

    print(f"{area}: {df.day.nunique()} days; products from {FIRST_HOUR}:00; variances in (EUR/MWh)^2")
    print(f"  deviation from day-ahead: var IDA1/2/3 = {np.round(total['var'], 1)}, cov12 = {total['cov12']:.1f}, "
          f"cov13 = {total['cov13']:.1f} (equal if the opening deviation persists), cov23 = {total['cov23']:.1f}")
    print(f"  opening {total['opening']:.1f}, auction noise {total['noise']:.1f}")
    print("  products k quarters apart, var of the difference / var of one product:")
    print("     k   diffusion  model    opening  model")
    for i, k in enumerate(LAGS):
        print(f"  {k:4d} {fitted['d'][0][i]:10.3f} {fitted['d'][1][i]:6.3f} {fitted['o'][0][i]:10.3f} {fitted['o'][1][i]:6.3f}")
    _, (early, late, idx), hours, index_obs = fitted["profile"]
    print(f"  diffusion 0..7 h: {total['early']:.0f} (model {early:.0f}); 7..19 h: {total['late']:.0f} (model {late:.0f})")
    print("  IDA3 to index, by delivery hour: " + ", ".join(f"{h}: {o:.0f} ({m:.0f})" for h, o, m in zip(hours, index_obs, idx)))
    print()
    print("Fitted SimulationSettings:")
    for name, value in params.items():
        print(f"  {name} = {value:.3g}")


if __name__ == "__main__":
    main()
