using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator
{
    public class MarketState
    {
        public PriceUnit EuroPerMWh = new PriceUnit(Currencies.Euro, Units.MegaWattHour);

        private UnitPrice _energyBuyPrice;
        private UnitPrice _energySellPrice;

        public MarketState()
        {
            
        }


        public UnitPrice EnergyBuyPrice
        {
            get => _energyBuyPrice;
            set => _energyBuyPrice = value;
        }
    }
}
