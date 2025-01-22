using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using BatterySimulator;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace BatterySimulatorGUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private BatterySimulator.BatterySimulator _simulator;
        private ObservableCollection<ISeries> _socSeries;
        private ObservableCollection<ISeries> _netChargeSeries;
        private ObservableCollection<DateTimePoint> _SoCvalues;
        private ObservableCollection<DateTimePoint> _netChargevalues;
        private DateTimeAxis _customAxis;
        private int _nHours;
        private int _maxItems = 100;
        
        public MainWindowViewModel()
        {
            _simulator = new BatterySimulator.BatterySimulator();
            _nHours = 24;

            _simulator.SetUp(_nHours ,(int) _simulator.Delta.TotalSeconds);

            _simulator.Recorder.EnergyContent.MaxItems = _maxItems;
            _SoCvalues = new ObservableCollection<DateTimePoint>();
            _simulator.Recorder.EnergyContent.CollectionChanged += EnergyContent_CollectionChanged;
            _socSeries = new ObservableCollection<ISeries>();
            _socSeries.Add(new LineSeries<DateTimePoint>
            {
                Values = _SoCvalues,
                Name = "SoC",
                Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 2 }, 
                Fill = new SolidColorPaint(SKColors.LightBlue) { StrokeThickness = 2 }, 
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true
            });

            _simulator.Recorder.NetCharge.MaxItems = 100;
            _netChargevalues = new ObservableCollection<DateTimePoint>();
            _simulator.Recorder.NetCharge.CollectionChanged += NetCharge_CollectionChanged;
            _netChargeSeries = new ObservableCollection<ISeries>();
            _netChargeSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _netChargevalues,
                Name = "Net charge",
                Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 2 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true
            });

            //_customAxis = new DateTimeAxis(TimeSpan.FromSeconds(10), Formatter)
            //{
            //    CustomSeparators = GetSeparators(),
            //    AnimationsSpeed = TimeSpan.FromMilliseconds(0),
            //    SeparatorsPaint = new SolidColorPaint(SKColors.Black.WithAlpha(100))
            //};

            //XAxes = [_customAxis];
        }

       

        public ObservableCollection<ISeries> SoC => _socSeries;
        public ObservableCollection<ISeries> NetCharge => _netChargeSeries;

        public async Task StartSimulation()
        {
            await Task.Run(ReadData);
        }

        private async Task ReadData()
        {
            _simulator.SimulationEnabled = true;
            _simulator.IsRealTime = false;
            XAxes[0].MaxLimit = (_simulator.Start + TimeSpan.FromMinutes(600)).Ticks;
            XAxes[0].MinLimit = _simulator.Start.Ticks;
            await Task.Run(_simulator.Simulate);
        }

        public void StopSimulation()
        {
            _simulator.SimulationEnabled = false;
        }

        private void NetCharge_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var series = sender as ObservableTimeSeries;
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (KeyValuePair<DateTime,double> datapoint in e.NewItems.Cast<KeyValuePair<DateTime,double>>())
                {
                    _netChargevalues.Add(new DateTimePoint(datapoint.Key, datapoint.Value));
                }

                if (_netChargevalues.Count > _maxItems)
                {
                    _netChargevalues.RemoveAt(0);
                }
                
            }
        }

        private void EnergyContent_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var series = sender as ObservableTimeSeries;
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (KeyValuePair<DateTime,double> datapoint in e.NewItems.Cast<KeyValuePair<DateTime,double>>())
                {
                    _SoCvalues.Add(new DateTimePoint(datapoint.Key, datapoint.Value));
                }

                if (_SoCvalues.Count > _maxItems)
                {
                    XAxes[0].MaxLimit = null;
                    XAxes[0].MinLimit = null;
                    _SoCvalues.RemoveAt(0);
                }
                
            }

        }


        private static string Formatter(DateTime date)
        {
            
            return date.ToShortTimeString();
        }

        private static double[] GetSeparators()
        {
            var now = DateTime.Now;

            return
            [
                now.AddSeconds(-25).Ticks,
                now.AddSeconds(-20).Ticks,
                now.AddSeconds(-15).Ticks,
                now.AddSeconds(-10).Ticks,
                now.AddSeconds(-5).Ticks,
                now.Ticks
            ];
        }

        public Axis[] XAxes { get; set; }
            =
            [
                new DateTimeAxis(TimeSpan.FromSeconds(10),Formatter)
                {
                  //  Name = "Time",
                }
            ];
           
        public Axis[] SoCAxes { get; set; }
            =
            [
                new Axis
                {
                    Name = "SoC (%)",
                    NamePaint = new SolidColorPaint(SKColors.White), 
                    MinLimit = 0, 
                    MaxLimit = 100,

                    LabelsPaint = new SolidColorPaint(SKColors.Green), 
                    TextSize = 10,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) 
                    { 
                        StrokeThickness = 2, 
                        PathEffect = new DashEffect(new float[] { 3, 3 }) 
                    } 
                }
            ];

        public Axis[] netChargeAxes { get; set; }
            = new Axis[]
            {
                new Axis
                {
                    Name = "Net charge (MW)",
                    NamePaint = new SolidColorPaint(SKColors.White), 
                    
                    LabelsPaint = new SolidColorPaint(SKColors.Green), 
                    TextSize = 10,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) 
                    { 
                        StrokeThickness = 2, 
                        PathEffect = new DashEffect(new float[] { 3, 3 }) 
                    } 
                }
            };

        public string TimeStepSeconds
        {
            get => _simulator.Delta.TotalSeconds.ToString();
            set
            {
                int nSeconds = 300;
                bool ok = int.TryParse(value, out nSeconds);
                if (ok)
                {
                    _simulator.Delta = TimeSpan.FromSeconds(nSeconds);
                }
            }
        }
    }
}
