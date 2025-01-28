using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BatterySimulator
{
    public class PnLManager
    {
        TimeSeries _sellIncome;
        TimeSeries _buyCost;
        TimeSeries _profit;
        TimeSeries _accProfit;

        public PnLManager()
        {
            _sellIncome = new TimeSeries();
            _buyCost = new TimeSeries();
            _profit = new TimeSeries();
            _accProfit = new TimeSeries();
        }

        public TimeSeries SellIncome
        {
            get => _sellIncome;
            set => _sellIncome = value;
        }

        public TimeSeries BuyCost
        {
            get => _buyCost;
            set => _buyCost = value;
        }

        public TimeSeries Profit
        {
            get => _profit;
            set => _profit = value;
        }

        public TimeSeries AccProfit
        {
            get => _accProfit;
            set => _accProfit = value;
        }

        public void UpdatePnL(DateTime t, Energy soldEnergy, Energy boughtEnergy, UnitPrice sellPrice, UnitPrice buyPrice)
        {
            _buyCost.SetValueAt(t, boughtEnergy.Value * buyPrice.Price);
            _sellIncome.SetValueAt(t, soldEnergy.Value * sellPrice.Price);
            _profit.SetValueAt(t, _sellIncome[t] - _buyCost[t]);
            _accProfit.SetValueAt(t, _accProfit[t - TimeSpan.FromSeconds(1)] + _profit[t]);
        }
    }
}
