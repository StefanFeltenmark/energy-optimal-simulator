using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
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
        
        public object Sync { get; } = new object();

        public MainWindowViewModel()
        {
            _simulator = new BatterySimulator.BatterySimulator();
            _simulator.SetUp(24,300);
           
            _simulator.Recorder.EnergyContent.MaxItems = 100;
            _SoCvalues = new ObservableCollection<DateTimePoint>();
            _simulator.Recorder.EnergyContent.CollectionChanged += EnergyContent_CollectionChanged;
            _socSeries = new ObservableCollection<ISeries>();
            _socSeries.Add(new LineSeries<DateTimePoint>
            {
                Values = _SoCvalues,
                Name = "SoC",
                Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 2 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                
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
                
            });
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

                if (_netChargevalues.Count > 100)
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

                if (_SoCvalues.Count > 100)
                {
                    _SoCvalues.RemoveAt(0);
                }
                
            }

        }

        public Axis[] XAxes { get; set; }
            = new Axis[]
            {
                new Axis
                {
                    Name = "Time",
                    NamePaint = new SolidColorPaint(SKColors.Black), 
                    UnitWidth = TimeSpan.FromMinutes(15).Ticks,
                    MaxLimit = null,
                    MinLimit = null,
                    LabelsPaint = new SolidColorPaint(SKColors.Blue), 

                    TextSize = 10,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) { StrokeThickness = 2 }  
                }
            };

        public Axis[] SoCAxes { get; set; }
            = new Axis[]
            {
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
            };

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
            await Task.Run(_simulator.Simulate);
        }

        public void StopSimulation()
        {
            _simulator.SimulationEnabled = false;
        }
    }
}
