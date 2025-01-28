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
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using OfficeOpenXml.FormulaParsing.Excel.Functions.DateTime;
using ReactiveUI;
using SkiaSharp;

namespace BatterySimulatorGUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private BatterySimulator.BatterySimulator _simulator;
        private ObservableCollection<ISeries> _socSeries;
        private ObservableCollection<ISeries> _netChargeSeries;
        private ObservableCollection<ISeries> _pnlSeries;
        private ObservableCollection<DateTimePoint> _SoCvalues;
        private ObservableCollection<DateTimePoint> _price;
        private ObservableCollection<DateTimePoint> _netChargevalues;
        private ObservableCollection<DateTimePoint> _pnlValues;
        private DateTime _simulationTime;
        private int _nHours;
        private int _maxItems = 200;
        
        public MainWindowViewModel()
        {
            _simulator = new BatterySimulator.BatterySimulator();
            _nHours = 24;

            _simulator.Recorder.PropertyChanged += Recorder_PropertyChanged;

            _simulator.SetUp(_nHours , 100);

            _simulator.Recorder.SoC.MaxItems = _maxItems;
            _SoCvalues = new ObservableCollection<DateTimePoint>();
            _simulator.Recorder.SoC.CollectionChanged += SoC_CollectionChanged;
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
                Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 3 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true,
                ScalesYAt = 0
            });

            _price = new ObservableCollection<DateTimePoint>();
            _netChargeSeries.Add(new LineSeries<DateTimePoint>
            {
                Values = _price,
                Name = "Price",
                Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 2 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true,
                ScalesYAt = 1
            });
            
            _pnlSeries = new ObservableCollection<ISeries>();
            _pnlValues = new ObservableCollection<DateTimePoint>();
            
            _simulator.PnlManager.PnL.CollectionChanged += 

            PnlSeries.Add(new LineSeries<DateTimePoint>
            {
                Values = _pnlValues,
                Name = "PnL",
                Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 2 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true,
                ScalesYAt = 0
            });

            _simulator.TimeProvider.PropertyChanged += TimeProvider_PropertyChanged;
            _simulationTime = _simulator.TimeProvider.GetTime();
        }

        private void Recorder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            
        }

        private void TimeProvider_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SimulationTime = _simulator.TimeProvider.GetTime();
        }

        
        public DateTime SimulationTime
        {
            get => _simulationTime; 
            set => this.RaiseAndSetIfChanged(ref _simulationTime, value); 
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
            SetPrice(_simulator.Start, _simulator.Start + TimeSpan.FromHours(24));
            XAxes[0].MaxLimit = (_simulator.Start + TimeSpan.FromHours(6)).Ticks;
            XAxes[0].MinLimit = _simulator.Start.Ticks;
            XAxes[0].ForceStepToMin = false;
            XAxes[0].MinStep = TimeSpan.FromSeconds(600).Ticks;
            await Task.Run(_simulator.Simulate);
        }

        public void SetPrice(DateTime fromTime, DateTime toTime)
        {
            DateTime t = fromTime;
            _price.Clear();
            while (t < toTime)
            {
                _price.Add(new DateTimePoint(t, _simulator.PriceForecaster.PriceForecast[t]));
                t += TimeSpan.FromMinutes(15);
            }
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

        private void SoC_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
                    _SoCvalues.RemoveAt(0);

                    XAxes[0].MinLimit = _SoCvalues.First().DateTime.Ticks;
                    XAxes[0].MaxLimit = _SoCvalues.Last().DateTime.Ticks;
                }
                
            }

        }


        private static string Formatter(DateTime date)
        {
            return date.ToShortTimeString();
        }

        
        public Axis[] XAxes { get; set; }
            =
            [
                new DateTimeAxis(TimeSpan.FromSeconds(1),Formatter)
                {
                  ShowSeparatorLines = true,
                  TicksAtCenter = false,
                  SeparatorsAtCenter = false,
                  UnitWidth = TimeSpan.FromSeconds(1).Ticks,
                  TextSize = 20
                },
               
            ];
           
        public Axis[] SoCAxes { get; set; }
            =
            [
                new Axis
                {
                    Name = "SoC (%)",
                    
                    MinLimit = 0, 
                    MaxLimit = 100,

                    LabelsPaint = new SolidColorPaint(SKColors.Green), 
                    TextSize = 20,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) 
                    { 
                        StrokeThickness = 2, 
                        PathEffect = new DashEffect(new float[] { 3, 3 }) 
                    } 
                }
            ];

        public Axis[] netChargeAxes { get; set; }
            = [
                new Axis
                {
                    Name = "Net charge (MW)",
                    
                    LabelsPaint = new SolidColorPaint(SKColors.Green), 
                    TextSize = 20,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) 
                    { 
                        StrokeThickness = 2, 
                        PathEffect = new DashEffect(new float[] { 3, 3 }) 
                    } 
                },
                new Axis
                {
                    Name = "Price",
                }
            ];

        public Axis[] pnlAxes { get; set; }
            = new Axis[]
            {
                new Axis
                {
                    Name = "PnL",
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

        public ObservableCollection<ISeries> PnlSeries
        {
            get => _pnlSeries;
            set => _pnlSeries = value;
        }
    }
}
