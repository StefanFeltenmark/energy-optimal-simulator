using Domain;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator
{
    public class PnLManager
    {
        ObservableTimeSeries _sellIncome;
        ObservableTimeSeries _buyCost;
        ObservableTimeSeries _profit;
        ObservableTimeSeries _accProfit;
        ObservableTimeSeries _wearCost;
        double _accWearCost;
        double _accEnvelopeWearCost;
        double _capacityLostMWh;
        BatteryDataRecorder _recorder;
        DateTime _latestTime;

        public PnLManager(BatteryDataRecorder recorder)
        {
            _sellIncome = new ObservableTimeSeries();
            _buyCost = new ObservableTimeSeries();
            _profit = new ObservableTimeSeries();
            _accProfit = new ObservableTimeSeries();
            _wearCost = new ObservableTimeSeries();

            _recorder = recorder;
        }

        public void SetUp(DateTime t)
        {
            _latestTime = t;
            _sellIncome.Add(t, 0);
            _buyCost.Add(t, 0);
            _profit.Add(t, 0);
            _accProfit.Add(t, 0);
            _wearCost.Add(t, 0);
            _accWearCost = 0;
            _accEnvelopeWearCost = 0;
            _capacityLostMWh = 0;
        }

        public ObservableTimeSeries SellIncome => _sellIncome;

        public ObservableTimeSeries BuyCost => _buyCost;

        /// <summary>Profit per step: sell income - buy cost - wear cost.</summary>
        public ObservableTimeSeries Profit => _profit;

        public ObservableTimeSeries AccProfit => _accProfit;

        /// <summary>Cost of battery wear per step, EUR (0 without a wear model).</summary>
        public ObservableTimeSeries WearCost => _wearCost;

        /// <summary>Total cost of battery wear so far, EUR.</summary>
        public double AccWearCost => _accWearCost;

        /// <summary>
        /// Total wear cost as POMA reads the wear map (its cheapest mix of points), EUR; never above <see cref="AccWearCost"/>.
        /// The difference is wear POMA does not see.
        /// </summary>
        public double AccEnvelopeWearCost => _accEnvelopeWearCost;

        /// <summary>Energy capacity lost to wear so far, MWh.</summary>
        public double CapacityLostMWh => _capacityLostMWh;

        /// <summary>Books cash flows that were settled directly (order-book fills, imbalance), both >= 0, and the step's wear.</summary>
        public void RecordCashFlows(DateTime t, double buyCost, double sellIncome, Wear wear = default)
        {
            _buyCost.Add(t, buyCost);
            _sellIncome.Add(t, sellIncome);
            _profit.Add(t, sellIncome - buyCost - wear.Cost);
            _accProfit.Add(t, _accProfit[_latestTime] + sellIncome - buyCost - wear.Cost);
            Book(t, wear);
        }

        public void UpdatePnL(DateTime t, UnitPrice buyPrice, UnitPrice sellPrice, Wear wear = default)
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

            _profit.Add(t, _sellIncome[t] - _buyCost[t] - wear.Cost);
            _accProfit.Add(t, _accProfit[_latestTime] + _profit[t]);
            Book(t, wear);
        }

        private void Book(DateTime t, Wear wear)
        {
            _wearCost.Add(t, wear.Cost);
            _accWearCost += wear.Cost;
            _accEnvelopeWearCost += wear.EnvelopeCost;
            _capacityLostMWh += wear.CapacityLostMWh;
            _latestTime = t;
        }
    }

    /// <summary>
    /// Battery wear over a step: its cost, EUR, the energy capacity lost, MWh, and the cost as POMA reads the wear map, EUR.
    /// </summary>
    public readonly record struct Wear(double Cost, double CapacityLostMWh, double EnvelopeCost = 0);
}
