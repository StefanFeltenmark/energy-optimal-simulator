using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace BatterySimulator
{
    public class SimulationTimeProvider : ITimeProvider, INotifyPropertyChanged
    {
        private DateTime _time;
        private TimeSpan _timeStep;

        public SimulationTimeProvider(DateTime start, TimeSpan timeStep)
        {
            _time = start;
            _timeStep = timeStep;
        }

        public TimeSpan TimeStep
        {
            get => _timeStep;
            set => _timeStep = value;
        }


        public void Increment()
        {
            _time += _timeStep;
            OnPropertyChanged();
        }

        public DateTime GetTime()
        {
            return _time;
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
