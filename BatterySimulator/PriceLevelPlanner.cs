
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class PriceLevelPlanner : IBatteryPlanner
    {
        private TimeSeries _plan;
        private Battery _battery;
        private Random _r = new Random(297349);
        private TimeSeries _energyPriceForecastForecast;
        private Percentage _buyPriceThreshold;
        private  Percentage _sellPriceThreshold;

        public PriceLevelPlanner(Battery battery)
        {
            _plan = new TimeSeries();
            _battery = battery;
            _buyPriceThreshold = new Percentage(10);
            _sellPriceThreshold = new Percentage(10);
        }

        public TimeSeries Plan
        {
            get => _plan;
            set => _plan = value;
        }

        public TimeSeries EnergyPriceForecast
        {
            get => _energyPriceForecastForecast;
            set => _energyPriceForecastForecast = value;
        }

        public Percentage BuyPriceThreshold
        {
            get => _buyPriceThreshold;
            set => _buyPriceThreshold = value;
        }

        public Percentage SellPriceThreshold
        {
            get => _sellPriceThreshold;
            set => _sellPriceThreshold = value;
        }


        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time], Units.MegaWatt);
        }

        public void UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            // update
            _plan = new TimeSeries(true, true);
            

            double maxPrice = _energyPriceForecastForecast.Values().Max();
            double minPrice = _energyPriceForecastForecast.Values().Min();

            var buyat = minPrice + _buyPriceThreshold.ToFraction() * (maxPrice - minPrice);
            var sellat = maxPrice - _sellPriceThreshold.ToFraction() * (maxPrice - minPrice);

            PlanningPeriod planningPeriod = new PlanningPeriod(planStart, resolution, nPeriods);
            foreach (PlanningInterval simulationInterval in planningPeriod.Intervals)
            {
                double prod = 0.0;
                double price = _energyPriceForecastForecast[simulationInterval.Start];

                if(price <= buyat)
                {
                    prod = _battery.CapacityC().ConvertToUnit(Units.MegaWatt).Value;
                }
                else if (price > sellat)
                {
                    prod = -_battery.CapacityC().ConvertToUnit(Units.MegaWatt).Value;
                }
                
                _plan.SetValueAt(simulationInterval.Start, prod);
            }
        }
    }
}
