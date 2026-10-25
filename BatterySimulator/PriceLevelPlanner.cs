
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class PriceLevelPlanner : IBatteryPlanner
    {
        private TimeSeries _plan;
        private Battery? _battery;
        private Random _r = new Random(297349);
        private TimeSeries _energyPriceForecastForecast;
        private ObservableTimeSeries _plannedSoC;
        private Percentage _buyPriceThreshold;
        private  Percentage _sellPriceThreshold;
        private TimeSpan _lookaAhead;

        public string Name => "PriceLevelPlanner";


        public PriceLevelPlanner()
        {
            _plan = new TimeSeries();
            _buyPriceThreshold = new Percentage(15);
            _sellPriceThreshold = new Percentage(15);
            _lookaAhead = TimeSpan.FromHours(6);
            _plannedSoC = new ObservableTimeSeries();
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

        public Battery Battery1
        {
            get => _battery;
            set => _battery = value;
        }

        public TimeSpan LookaAhead
        {
            get => _lookaAhead;
            set => _lookaAhead = value;
        }


        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time], Units.MegaWatt);
        }

        public ObservableTimeSeries PlannedSoC
        {
            get { return _plannedSoC; }
        }

        public async Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            // update
            _plan = new TimeSeries(true, true);
            
            double maxPrice = _energyPriceForecastForecast.GetSubSeries(planStart,planStart+LookaAhead).Values().Max();
            double minPrice = _energyPriceForecastForecast.GetSubSeries(planStart,planStart+LookaAhead).Values().Min();

            var buyat = minPrice + _buyPriceThreshold.ToFraction() * (maxPrice - minPrice);
            var sellat = maxPrice - _sellPriceThreshold.ToFraction() * (maxPrice - minPrice);

            // Step through local clock times: the prices and the plan are keyed by them. (PlanningPeriod's
            // intervals are UTC and would shift both lookups by an hour.)
            for (int k = 0; k < nPeriods; k++)
            {
                DateTime t = planStart + k * resolution;
                double prod = 0.0;
                double price = _energyPriceForecastForecast[t];

                if(price <= buyat)
                {
                    prod = -Battery1.InitialCapacityC.CovertToUnit(Units.MegaWatt).Value;
                }
                else if (price > sellat)
                {
                    prod = Battery1.InitialCapacityC.CovertToUnit(Units.MegaWatt).Value;
                }
                
                _plan.SetValueAt(t, prod);
            }
        }
    }
}
