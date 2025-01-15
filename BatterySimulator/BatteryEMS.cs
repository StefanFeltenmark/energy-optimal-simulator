using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Math;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Thermal.Quantities;
using Power = Powel.Optimal.MultiAsset.Domain.Thermal.Quantities.Power;

namespace BatterySimulator
{
    public class BatteryEMS : IObservable<BatteryState>
    {
        private Battery _battery;
        private BatteryState _state;
        private List<IObserver<BatteryState>> observers = new List<IObserver<BatteryState>>();


        public BatteryEMS(Battery battery, BatteryState initialState)
        {
            _battery = battery;
            _state = initialState;
        }

        public Battery Battery1
        {
            get => _battery;
        }

        public void SetChargeLevel(Power setpoint)
        {
            _state.Charging = (setpoint.Value > 0)? setpoint:0.0;
            _state.Discharging = (setpoint.Value < 0)?-setpoint:0.0;
        }

        public Power GetChargeLevel()
        {
            return _state.Charging - _state.Discharging;
        }

        public Percentage GetSoC()
        {
            var soc = _state.EnergyContent / _battery.CapacityE();
            return soc;
        }

        
        public void UpdateState(Time delta)
        {
            _state.EnergyContent +=
                _state.Charging * delta - _state.Discharging * delta;

            // Truncate to limits
            if (_state.EnergyContent.Value < 0)
            {
                _state.EnergyContent = 0.0;
            }

            if(_state.EnergyContent > _battery.NominalEnergyCapacity)
            {
                _state.EnergyContent = _battery.NominalEnergyCapacity;
            }

            // Capacity reduction

        }

        public IDisposable Subscribe(IObserver<BatteryState> observer)
        {
            if (!observers.Contains(observer))
                observers.Add(observer);
            return new Unsubscriber(observers, observer);
        }

        private class Unsubscriber : IDisposable
        {
            private List<IObserver<BatteryState>>_observers;
            private IObserver<BatteryState> _observer;

            public Unsubscriber(List<IObserver<BatteryState>> observers, IObserver<BatteryState> observer)
            {
                this._observers = observers;
                this._observer = observer;
            }

            public void Dispose()
            {
                if (_observer != null && _observers.Contains(_observer))
                    _observers.Remove(_observer);
            }
        }
    }
}
