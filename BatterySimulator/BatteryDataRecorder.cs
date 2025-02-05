using System.ComponentModel;
using System.Runtime.CompilerServices;
using Domain;
using Powel.Optimal.MultiAsset.Domain.Quantities;

namespace BatterySimulator
{
    public class BatteryDataRecorder : IObserver<BatteryState>, INotifyPropertyChanged
    {
        private IDisposable unsubscriber;
        private ITimeProvider _timeProvider;
        private ObservableTimeSeries _SoC = new ObservableTimeSeries();
        private ObservableTimeSeries _energyContent = new ObservableTimeSeries();
        private ObservableTimeSeries _netCharge = new ObservableTimeSeries();
        private ObservableTimeSeries _chargingGrid = new ObservableTimeSeries();
        private ObservableTimeSeries _dischargingGrid = new ObservableTimeSeries();
        private ObservableTimeSeries _chargingBattery = new ObservableTimeSeries();
        private ObservableTimeSeries _dischargingBattery = new ObservableTimeSeries();

        
        private int _updated;
        public int Updated
        {
            set { SetField(ref _updated, value, "Updated"); }
        }

        public object Sync { get; } = new object();

        public BatteryDataRecorder(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public ObservableTimeSeries EnergyContent => _energyContent;

        public ObservableTimeSeries NetCharge => _netCharge;

        public ObservableTimeSeries SoC => _SoC;

        public ObservableTimeSeries ChargingGrid => _chargingGrid;

        public ObservableTimeSeries DischargingGrid => _dischargingGrid;

        public ObservableTimeSeries ChargingBattery => _chargingBattery;

        public ObservableTimeSeries DischargingBattery => _dischargingBattery;


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
                _netCharge.Add(_timeProvider.GetTime(), value.ChargingGrid.ConvertToUnit(Units.MegaWatt).Value-value.DischargingGrid.ConvertToUnit(Units.MegaWatt).Value);
                _chargingGrid.Add(_timeProvider.GetTime(), value.ChargingGrid.ConvertToUnit(Units.MegaWatt).Value);
                _dischargingGrid.Add(_timeProvider.GetTime(), -value.DischargingGrid.ConvertToUnit(Units.MegaWatt).Value);
                _chargingBattery.Add(_timeProvider.GetTime(), value.ChargingBattery.ConvertToUnit(Units.MegaWatt).Value);
                _dischargingBattery.Add(_timeProvider.GetTime(), -value.DischargingBattery.ConvertToUnit(Units.MegaWatt).Value);
                Updated = _updated + 1;
            }
        }

        protected virtual void Unsubscribe()
        {
            unsubscriber.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
