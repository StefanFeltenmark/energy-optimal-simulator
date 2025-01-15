using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Powel.Optimal.MultiAsset.Domain.General.Data;

namespace BatterySimulator
{
    public class DataRecorder : IObserver<BatteryState>
    {
        private IDisposable unsubscriber;
        private ITimeProvider _timeProvider;
        private TimeSeries _energyContent = new TimeSeries();

        public DataRecorder(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public TimeSeries EnergyContent
        {
            get => _energyContent;
            set => _energyContent = value;
        }

        public void SetUpRecording()
        {

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
            _energyContent.SetValueAt(_timeProvider.GetTime(), value.EnergyContent.Value);
        }

        public virtual void Unsubscribe()
        {
            unsubscriber.Dispose();
        }
    }
}
