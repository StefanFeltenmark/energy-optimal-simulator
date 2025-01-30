using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator
{
    public class PnLManager
    {
        ObservableTimeSeries _sellIncome;
        ObservableTimeSeries _buyCost;
        ObservableTimeSeries _profit;
        ObservableTimeSeries _accProfit;
        BatteryDataRecorder _recorder;
        DateTime _latestTime;

        public PnLManager(BatteryDataRecorder recorder)
        {
            _sellIncome = new ObservableTimeSeries();
            _buyCost = new ObservableTimeSeries();
            _profit = new ObservableTimeSeries();
            _accProfit = new ObservableTimeSeries();
            
            _recorder = recorder;
        }

        public void SetUp(DateTime t)
        {
            _latestTime = t;
            _sellIncome.Add(t, 0);
            _buyCost.Add(t, 0);
            _profit.Add(t, 0);
            _accProfit.Add(t, 0);
        }

        public ObservableTimeSeries SellIncome => _sellIncome;

        public ObservableTimeSeries BuyCost => _buyCost;

        public ObservableTimeSeries Profit => _profit;

        public ObservableTimeSeries AccProfit => _accProfit;

        public void UpdatePnL(DateTime t, UnitPrice buyPrice, UnitPrice sellPrice)
        {
            Energy boughtEnergy = 0;
            Energy soldEnergy = 0;
            Power netCharge = new Power(_recorder.NetCharge[t], Units.MegaWatt);
            Time delta = new Time((t - _latestTime).TotalHours, Units.Hour);
            if(netCharge.Value < -0.001)
            {
                soldEnergy = (-netCharge*delta).ToUnit(Units.MegaWattHour);
                boughtEnergy = new Energy(0);
            }
            else if(netCharge.Value > 0.0001)
            {
                soldEnergy = new Energy(0);
                boughtEnergy = (netCharge*delta).ToUnit(Units.MegaWattHour);
            }
            _buyCost.Add(t, boughtEnergy.Value * buyPrice.Price);
            _sellIncome.Add(t, soldEnergy.Value * sellPrice.Price);
            
            _profit.Add(t, _sellIncome[t] - _buyCost[t]);

            double accProf = _accProfit[_latestTime];
            accProf += _profit[t];
            _accProfit.Add(t, accProf);

            _latestTime = t;
        }
    }
}
