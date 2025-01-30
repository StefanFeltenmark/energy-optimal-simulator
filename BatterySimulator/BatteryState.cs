using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class BatteryState
    {
        
        private Energy _energyContent = new Energy(0,Units.MegaWattHour);
        private Energy _capacity = new Energy(0, Units.MegaWattHour);
        private Power _setPoint = new Power(0, Units.MegaWatt);
        private Power _chargeCapacity = new Power(0, Units.MegaWatt);
        private Power _charging = new Power(0, Units.MegaWatt);
        private Power _discharging = new Power(0, Units.MegaWatt);
        private Percentage _SoC = new Percentage(0);

        public Energy EnergyContent
        {
            get => _energyContent;
            set => _energyContent = value;
        }

        public Energy Capacity
        {
            get => _capacity;
            set => _capacity = value;
        }

        public Power Charging
        {
            get => _charging;
            set => _charging = value;
        }

        public Power Discharging
        {
            get => _discharging;
            set => _discharging = value;
        }

        public Power ChargeCapacity
        {
            get => _chargeCapacity;
            set => _chargeCapacity = value;
        }

        public Percentage SoC
        {
            get => _SoC;
            set => _SoC = value;
        }

        public Power SetPoint
        {
            get => _setPoint;
            set => _setPoint = value;
        }
    }
}
