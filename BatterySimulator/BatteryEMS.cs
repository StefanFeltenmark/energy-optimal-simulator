﻿using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class BatteryEMS : IObservable<BatteryState>
    {
        private Battery _battery;
        private BatteryState _state;
        private DateTime _lastestTime;
        private List<IObserver<BatteryState>> observers = new List<IObserver<BatteryState>>();

        public BatteryEMS(Battery battery, BatteryState initialState, DateTime start)
        {
            _battery = battery;
            _state = initialState;
            _lastestTime = start;
        }

        public Battery Battery1
        {
            get => _battery;
        }

        public void SetChargeLevel(Power setpoint)
        {
            _state.Charging = (setpoint.Value > 0)? setpoint:0.0;
            _state.Discharging = (setpoint.Value < 0)? new Power(-setpoint.Value, Units.MegaWatt):0.0;
        }

        public Power GetChargeLevel()
        {
            return _state.Charging - _state.Discharging;
        }

        public Percentage GetSoC()
        {
            Percentage soc = _state.EnergyContent / _battery.CapacityE();
            return soc;
        }

        
        public async Task UpdateState(DateTime time)
        {
            Time delta = new Time((time -_lastestTime).TotalHours, Units.Hour);
            
            // At most one of charging and discharging is non-zero
            Energy deltaMinus = _state.Discharging * delta;
            Energy deltaplus = _state.Charging * delta;

            Energy deltaMinusBattery = (1/ _battery.DischargeEfficiency) * deltaMinus;
            Energy deltaPlusBattery = _battery.ChargeEfficiency * deltaplus;

            Energy deltaBattery = deltaPlusBattery - deltaMinusBattery;

            _state.EnergyContent += deltaPlusBattery - deltaMinusBattery; 

            // Truncate to limits
            if (_state.EnergyContent + deltaBattery < new Energy(0.0))
            {
                _state.EnergyContent = new Energy(0, Units.MegaWattHour);
            }

            if(_state.EnergyContent + deltaBattery > _battery.NominalEnergyCapacity)
            {
                _state.EnergyContent = _battery.NominalEnergyCapacity.ConvertToUnit(Units.MegaWattHour);
            }

            _state.SoC = GetSoC();

            await PushBatteryState(_state);

            _lastestTime = time;

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

        public async Task PushBatteryState(BatteryState state)
        {
            foreach (var observer in observers) {
                  observer.OnNext(state);
            }
        }

        public void EndTransmission()
        {
            foreach (var observer in observers.ToArray())
                if (observers.Contains(observer))
                    observer.OnCompleted();

            observers.Clear();
        }
    }
}
