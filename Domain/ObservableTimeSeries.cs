using System.Collections;
using System.Collections.Specialized;

using Powel.Optimal.MultiAsset.Domain.General.Data;

namespace BatterySimulator
{
    public class ObservableTimeSeries : INotifyCollectionChanged, IReadOnlyCollection<KeyValuePair<DateTime,double>>
    {
        private TimeSeries _ts;
        private int _maxItems;

        public ObservableTimeSeries(TimeSeries ts)
        {
            _ts = ts;
        }

        public ObservableTimeSeries(double defaultvalue = 0.0, bool isBreakpoint = true)
        {
            _ts = new TimeSeries();
            _ts.DefaultValue = defaultvalue;
            _ts.IsBreakPointSeries = isBreakpoint;
            _maxItems = int.MaxValue;
        }

        public void Add(DateTime t, double val)
        {
            _ts.AddValueAt(t,val);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,new KeyValuePair<DateTime,double>(t,val)));
        }

        public double this[DateTime t]
        {
            get => _ts[t];
        }

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public IEnumerator<KeyValuePair<DateTime, double>> GetEnumerator()
        {
            List<KeyValuePair<DateTime, double>> list = _ts.ToArray().Take(_maxItems).ToList();
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count { get; set; }

        public int MaxItems
        {
            get => _maxItems;
            set => _maxItems = value;
        }

        public TimeSeries Series
        {
            get => _ts;
            set
            {
                _ts = value;
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }
    }
}
