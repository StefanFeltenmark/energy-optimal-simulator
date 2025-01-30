using System.ComponentModel;
using System.Runtime.CompilerServices;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator
{
    public class DataRecorder : IObserver<BatteryState>, INotifyPropertyChanged
    {
        private IDisposable unsubscriber;
        private ITimeProvider _timeProvider;
        private ObservableTimeSeries _SoC = new ObservableTimeSeries();
        private ObservableTimeSeries _energyContent = new ObservableTimeSeries();
        private ObservableTimeSeries _netCharge = new ObservableTimeSeries();
        private ObservableTimeSeries _charging = new ObservableTimeSeries();
        private ObservableTimeSeries _discharging = new ObservableTimeSeries();
        

        private int _updated;
        public int Updated
        {
            set { SetField(ref _updated, value, "Updated"); }
        }

        public object Sync { get; } = new object();

        public DataRecorder(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;

        }

        public ObservableTimeSeries EnergyContent
        {
            get => _energyContent;
        }

        public ObservableTimeSeries NetCharge
        {
            get => _netCharge;
        }

        public ObservableTimeSeries SoC
        {
            get => _SoC;
        }

        public ObservableTimeSeries Charging
        {
            get => _charging;
            
        }

        public ObservableTimeSeries Discharging
        {
            get => _discharging;
            
        }


        public virtual void Subscribe(IObservable<BatteryState> provider)
        {
            if (provider != null)
                unsubscriber = provider.Subscribe(this);
        }

        public void OnCompleted()
        {
            this.Unsubscribe();
            
        }

        public void OnError(Exception error)
        {
            throw new NotImplementedException();
        }

        public void OnNext(BatteryState value)
        {
            lock (Sync)
            {
                _energyContent.Add(_timeProvider.GetTime(), value.EnergyContent.ConvertToUnit(Units.MegaWattHour).Value);
                _SoC.Add(_timeProvider.GetTime(), value.SoC.Value);
                _netCharge.Add(_timeProvider.GetTime(), value.Charging.ConvertToUnit(Units.MegaWatt).Value-value.Discharging.ConvertToUnit(Units.MegaWatt).Value);
                _charging.Add(_timeProvider.GetTime(), value.Charging.ConvertToUnit(Units.MegaWatt).Value);
                _discharging.Add(_timeProvider.GetTime(), -value.Discharging.ConvertToUnit(Units.MegaWatt).Value);
                Updated = _updated + 1;
            }
        }

        public virtual void Unsubscribe()
        {
            unsubscriber.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
