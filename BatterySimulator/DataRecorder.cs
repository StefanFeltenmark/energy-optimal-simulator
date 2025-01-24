﻿using System;
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
        private ObservableTimeSeries _energyContent = new ObservableTimeSeries();
        private ObservableTimeSeries _netCharge = new ObservableTimeSeries();
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
                _energyContent.Add(_timeProvider.GetTime(), value.SoC.Value);
                _netCharge.Add(_timeProvider.GetTime(), value.Charging.Value-value.Discharging.Value);
            }
        }

        public virtual void Unsubscribe()
        {
            unsubscriber.Dispose();
        }
    }
}
