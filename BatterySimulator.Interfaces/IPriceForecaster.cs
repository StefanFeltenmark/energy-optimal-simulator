using Powel.Optimal.MultiAsset.Domain.General.Data;

namespace BatterySimulator;

public interface IPriceForecaster
{
    TimeSeries PriceForecast { get; set; }
    void UpdateForecast(DateTime fromTime, TimeSpan horizon, TimeSpan resolution);
}