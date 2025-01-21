using System.Collections;
using System.Collections.Specialized;
using Powel.Optimal.MultiAsset.Domain.General.Data;

namespace BatterySimulator
{
    public class ObservableTimeSeries : INotifyCollectionChanged, IReadOnlyCollection<double>
    {
        private TimeSeries _ts;

        public ObservableTimeSeries()
        {
            _ts = new TimeSeries();
        }

        public void Add(DateTime t, double val)
        {
            _ts.AddValueAt(t,val);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,val));
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public IEnumerator<double> GetEnumerator()
        {

            return _ts.Values().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get; set; }
    }
}
