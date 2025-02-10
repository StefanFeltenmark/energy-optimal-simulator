using BatterySimulator.Interfaces;
using Domain;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatterySimulator
{
    public class BatteryEMS : IBatteryEMS
    {
        private Battery _battery;
        private BatteryState _state;
        private DateTime _lastestObservationTime;
        private List<IObserver<BatteryState>> _observers = new();

        public BatteryEMS(Battery battery, BatteryState initialState, DateTime start)
        {
            _battery = battery;
            _state = initialState;
            _lastestObservationTime = start;
        }

        
        public void SetChargeLevel(Power setpoint)
        {
            _state.SetPoint = setpoint;
        }

        public Power GetChargeLevel()
        {
            return _state.ChargingGrid - _state.DischargingGrid;
        }

        public Percentage GetSoC()
        {
            Percentage soc = _state.EnergyContent / _battery.CapacityE();
            return soc;
        }

        
        public async Task UpdateState(DateTime time)
        {
            Time delta = new Time((time -_lastestObservationTime).TotalHours, Units.Hour);

            if(delta.Value <= 0.00001) return;
            
            _state.ChargingGrid = (_state.SetPoint.Value > 0)?  _state.SetPoint:0.0;
            _state.DischargingGrid = (_state.SetPoint.Value < 0)? -_state.SetPoint:0.0;

            // At most one of charging and discharging is non-zero
            Energy deltaMinusGrid = _state.DischargingGrid * delta;
            Energy deltaPlusGrid = _state.ChargingGrid * delta;

            Energy deltaMinusBattery = (1/ _battery.DischargeEfficiency) * deltaMinusGrid;
            Energy deltaPlusBattery = _battery.ChargeEfficiency * deltaPlusGrid;

            Energy dischargeLosses = (1/ _battery.DischargeEfficiency) * deltaMinusGrid - deltaMinusGrid;
            Energy chargeLosses = deltaPlusBattery -  _battery.ChargeEfficiency * deltaPlusGrid;

            Energy deltaBattery = deltaPlusBattery - deltaMinusBattery;

            
            // Truncate to limits
            if (_state.EnergyContent + deltaBattery <= new Energy(0.0))
            {
                Power toGrid = _state.EnergyContent*_battery.DischargeEfficiency/delta;
                _state.DischargingGrid = toGrid;
                Power fromBattery = toGrid/ _battery.DischargeEfficiency;
                _state.DischargingBattery = fromBattery;
                _state.EnergyContent = 0;
            }
            else if(_state.EnergyContent + deltaBattery >= _battery.NominalEnergyCapacity)
            {
                Power fromGrid = (_battery.NominalEnergyCapacity-_state.EnergyContent) /(_battery.ChargeEfficiency*delta);
                _state.ChargingGrid = fromGrid;
                Power toBattery = fromGrid * _battery.ChargeEfficiency;
                _state.ChargingBattery = toBattery;
                _state.EnergyContent = _battery.NominalEnergyCapacity;
            }
            else
            {
                _state.EnergyContent += deltaPlusBattery - deltaMinusBattery; 
                _state.ChargingBattery = _state.ChargingGrid*_battery.ChargeEfficiency;
                _state.DischargingBattery = _state.DischargingGrid/_battery.DischargeEfficiency;
            }

            _state.SoC = GetSoC();

            await PushBatteryState(_state);

            _lastestObservationTime = time;

        }

        public IDisposable Subscribe(IObserver<BatteryState> observer)
        {
            if (!_observers.Contains(observer))
                _observers.Add(observer);
            return new Unsubscriber(_observers, observer);
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
            foreach (var observer in _observers) {
                  observer.OnNext(state);
            }
        }

        public void EndTransmission()
        {
            foreach (var observer in _observers.ToArray())
                if (_observers.Contains(observer))
                    observer.OnCompleted();

            _observers.Clear();
        }
    }
}
