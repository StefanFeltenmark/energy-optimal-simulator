using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
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
            var soc = _state.EnergyContent / _battery.CapacityE();
            return soc;
        }

        
        public async Task UpdateState(DateTime time)
        {
            Time delta = new Time((time -_lastestTime).TotalHours, Units.Hour);
            
            _state.EnergyContent +=
                _state.Charging * delta - _state.Discharging * delta;

            // Truncate to limits
            if (_state.EnergyContent.Value < 0)
            {
                _state.EnergyContent = 0.0;
            }

            if(_state.EnergyContent.Value > _battery.NominalEnergyCapacity.Value)
            {
                _state.EnergyContent = _battery.NominalEnergyCapacity.Value;
            }

            PushBatteryState(_state);

            // Capacity reduction
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
