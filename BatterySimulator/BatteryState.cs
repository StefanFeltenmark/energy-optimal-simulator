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
        private Power _chargingGridGrid = new Power(0, Units.MegaWatt);
        private Power _dischargingGridGrid = new Power(0, Units.MegaWatt);
        private Power _chargingBattery = new Power(0, Units.MegaWatt);
        private Power _dischargingBattery = new Power(0, Units.MegaWatt);
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

        public Power ChargingGrid
        {
            get => _chargingGridGrid;
            set => _chargingGridGrid = value;
        }

        public Power DischargingGrid
        {
            get => _dischargingGridGrid;
            set => _dischargingGridGrid = value;
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

        public Power ChargingBattery
        {
            get => _chargingBattery;
            set => _chargingBattery = value;
        }

        public Power DischargingBattery
        {
            get => _dischargingBattery;
            set => _dischargingBattery = value;
        }
    }
}
