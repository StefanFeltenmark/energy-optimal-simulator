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
        
        private Energy _energyContent;
        private Energy _capacity;
        private Power _chargeCapacity;
        private Power _charging;
        private Power _discharging;

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
    }
}
