
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
        private double _buyPriceLevel;
        private double _sellPriceLevel;

        public PriceLevelPlanner(Battery battery)
        {
            _plan = new TimeSeries();
            _battery = battery;
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

        public double BuyPriceLevel
        {
            get => _buyPriceLevel;
            set => _buyPriceLevel = value;
        }

        public double SellPriceLevel
        {
            get => _sellPriceLevel;
            set => _sellPriceLevel = value;
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

            _buyPriceLevel = minPrice + 0.1 * (maxPrice - minPrice);
            _sellPriceLevel = maxPrice - 0.1 * (maxPrice - minPrice);

            PlanningPeriod planningPeriod = new PlanningPeriod(planStart, resolution, nPeriods);
            foreach (PlanningInterval simulationInterval in planningPeriod.Intervals)
            {
                double prod = 0.0;
                double price = _energyPriceForecastForecast[simulationInterval.Start];

                if(price <= _buyPriceLevel)
                {
                    prod = _battery.CapacityC().ConvertToUnit(Units.MegaWatt).Value;
                }
                else if (price > _sellPriceLevel)
                {
                    prod = -_battery.CapacityC().ConvertToUnit(Units.MegaWatt).Value;
                }
                
                _plan.SetValueAt(simulationInterval.Start, prod);
            }
        }
    }
}
