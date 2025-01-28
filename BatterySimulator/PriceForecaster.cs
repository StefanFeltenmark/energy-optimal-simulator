using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Powel.Optimal.MultiAsset.Domain.General.Data;

namespace BatterySimulator
{
    public class PriceForecaster : IPriceForecaster
    {
        private TimeSeries _priceForecast;
        private TimeSeries _truePrice;
        Random _r = new Random(1234);
        private double _a;
        private double _b;

        public PriceForecaster(TimeSeries truePrice, double a = 0.1, double b = 5.0)
        {
            _priceForecast = new TimeSeries();
            _truePrice = truePrice;
            _a = a;
            _b = b;
        }

        public TimeSeries PriceForecast
        {
            get => _priceForecast;
            set => _priceForecast = value;
        }
        public void UpdateForecast(DateTime fromTime, TimeSpan horizon, TimeSpan resolution)
        {
            DateTime t = fromTime;
            while (t < fromTime + horizon)
            {
                var price = _truePrice[t]*(1 + _a*(1-2*_r.NextDouble())) + _b*_r.NextDouble();
                _priceForecast.SetValueAt(t,price);
                t += resolution;
            }
        }
    }
}
