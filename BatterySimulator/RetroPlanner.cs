
using BatterySimulator.Interfaces;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class RetroPlanner : IBatteryPlanner
    {
        private TimeSeries _plan;
        private Battery? _battery;
        private Random _r = new Random(297349);
        private TimeSeries _historicPrices;
        private TimeSeries _priceForecast;
        private ObservableTimeSeries _plannedSoC;
        private Percentage _buyPriceThreshold;
        private  Percentage _sellPriceThreshold;
        private TimeSpan _lookBack;

        public string Name => "RetroPlanner";


        public RetroPlanner()
        {
            _plan = new TimeSeries();
            _buyPriceThreshold = new Percentage(15);
            _sellPriceThreshold = new Percentage(15);
            _lookBack = TimeSpan.FromHours(6);
            _plannedSoC = new ObservableTimeSeries();
            _priceForecast = new TimeSeries();
        }


        public TimeSeries Plan
        {
            get => _plan;
            set => _plan = value;
        }

        public TimeSeries HistoricPrices
        {
            get => _historicPrices;
            set => _historicPrices = value;
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

        public TimeSpan LookBack
        {
            get => _lookBack;
            set => _lookBack = value;
        }


        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time], Units.MegaWatt);
        }

        public ObservableTimeSeries PlannedSoC
        {
            get { return _plannedSoC; }
        }

        public TimeSeries PriceForecast
        {
            get => _priceForecast;
            set => _priceForecast = value;
        }

        public async Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            // update
            
            var newPlan = new TimeSeries(true, true);
            
            double maxPrice = _historicPrices.GetSubSeries(planStart-LookBack,planStart).Values().Max();
            double minPrice = _historicPrices.GetSubSeries(planStart-LookBack,planStart).Values().Min();

            var buyat = minPrice + _buyPriceThreshold.ToFraction() * (maxPrice - minPrice);
            var sellat = maxPrice - _sellPriceThreshold.ToFraction() * (maxPrice - minPrice);

            PlanningPeriod planningPeriod = new PlanningPeriod(planStart, resolution, nPeriods);
            foreach (PlanningInterval simulationInterval in planningPeriod.Intervals)
            {
                double prod = 0.0;
                double price = _priceForecast[simulationInterval.Start];

                if(price <= buyat)
                {
                    prod = -Battery1.CapacityC().ConvertToUnit(Units.MegaWatt).Value;
                }
                else if (price > sellat)
                {
                    prod = Battery1.CapacityC().ConvertToUnit(Units.MegaWatt).Value;
                }
                
                newPlan.SetValueAt(simulationInterval.Start, prod);
            }

            _plan = newPlan;
        }
    }
}
