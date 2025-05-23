﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using BatterySimulator;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using ReactiveUI;
using SkiaSharp;

namespace BatterySimulatorGUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private BatterySimulator.BatterySimulator? _simulator;
        private ObservableCollection<ISeries> _socSeries;
        private ObservableCollection<ISeries> _chargingSeries;
        private ObservableCollection<ISeries> _chargeSeries;
        private ObservableCollection<ISeries> _dischargeSeries;
        private ObservableCollection<ISeries> _pnlSeries;
        private ObservableCollection<DateTimePoint> _SoCvalues;
        
        private ObservableCollection<DateTimePoint> _SoCPlanValues;

        private ObservableCollection<DateTimePoint> _priceForecast;
        private ObservableCollection<DateTimePoint> _realizedPrice;
        private ObservableCollection<DateTimePoint> _netChargevalues;
        private ObservableCollection<DateTimePoint> _chargeValues;
        private ObservableCollection<DateTimePoint> _dischargeValues;
        private ObservableCollection<DateTimePoint> _pnlValues;
        
        private DateTime _simulationTime;
        private string? _currentPnL;
        private string? _equivalentCycles;
        private TimeSpan _visibleHorizon;
        private int _nHours;
        private int _maxItems = 180;

        public MainWindowViewModel()
        {
            
        }
        
        public MainWindowViewModel(BatterySimulator.BatterySimulator simulator)
        {
            _simulator = simulator;
            
            _nHours = 1000;
            _simulator.SetUp(_nHours , 300);

            _visibleHorizon = TimeSpan.FromHours(6);

            ManageHandlers();

            // Calculate max items
            _maxItems = (int) (_visibleHorizon.TotalMinutes / _simulator.Delta.TotalMinutes);

            _simulator.Recorder.SoC.MaxItems = _maxItems;
            _SoCvalues = new ObservableCollection<DateTimePoint>();
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

            _SoCPlanValues = new ObservableCollection<DateTimePoint>();
            _socSeries.Add(new LineSeries<DateTimePoint>
            {
                Values = _SoCPlanValues,
                Name = "SoC plan",
                Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 2 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true
            });


            
            _simulator.Recorder.ChargingGrid.MaxItems = _maxItems;
            _chargeValues = new ObservableCollection<DateTimePoint>();
            _chargingSeries = new ObservableCollection<ISeries>();
            _chargingSeries.Add(new ColumnSeries<DateTimePoint>
            {
                Values = _chargeValues,
                Name = "Charge",
                Stroke = new SolidColorPaint(SKColors.LightBlue) { StrokeThickness = 1 }, 
                Padding = 0,
                MaxBarWidth = double.MaxValue,
                IgnoresBarPosition = true,
                Fill = new SolidColorPaint(SKColors.LightBlue),
                IsVisibleAtLegend = false,
                ScalesYAt = 0
            });

            _simulator.Recorder.DischargingGrid.MaxItems = _maxItems;
            _dischargeValues = new ObservableCollection<DateTimePoint>();
            _chargingSeries.Add(new ColumnSeries<DateTimePoint>
            {
                Values = _dischargeValues,
                Name = "Discharge",
                Stroke = new SolidColorPaint(SKColors.LightGreen) { StrokeThickness = 1 },
                Padding = 0,
                Fill =  new SolidColorPaint(SKColors.LightGreen),
                MaxBarWidth = double.MaxValue,
                IgnoresBarPosition = true,
                IsVisibleAtLegend = true,
                ScalesYAt = 0
            });

            _priceForecast = new ObservableCollection<DateTimePoint>();
            _chargingSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _priceForecast,
                Name = "PriceForecast",
                Stroke = new SolidColorPaint(SKColors.LightPink) { StrokeThickness = 2 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true,
                ScalesYAt = 1
            });

            _realizedPrice = new ObservableCollection<DateTimePoint>();
            _chargingSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _realizedPrice,
                Name = "Price",
                Stroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 3 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true,
                ScalesYAt = 1
            });
            
            _pnlSeries = new ObservableCollection<ISeries>();
            _pnlValues = new ObservableCollection<DateTimePoint>();
            PnlSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _pnlValues,
                Name = "PnL (€)",
                Stroke = new SolidColorPaint(SKColors.Green) { StrokeThickness = 2 }, 
                Fill = new SolidColorPaint(SKColors.LightGreen) { StrokeThickness = 2 }, 
                GeometryFill = null,
                GeometryStroke = null,
                IsVisibleAtLegend = true,
                ScalesYAt = 0
            });

           
            _simulationTime = _simulator.TimeProvider.GetTime();

            netChargeAxes[0].MaxLimit = _simulator.Battery1.CapacityC().ConvertToUnit( Units.MegaWatt).Value + 1;
            netChargeAxes[0].MinLimit = -_simulator.Battery1.CapacityC().ConvertToUnit( Units.MegaWatt).Value - 1;

        }

        private void ManageHandlers(bool subScribe = true)
        {
            // Event handlers
            if (subScribe)
            {
                _simulator.Recorder.PropertyChanged += Recorder_PropertyChanged;
                _simulator.Recorder.SoC.CollectionChanged += SoC_CollectionChanged;
                _simulator.Recorder.ChargingGrid.CollectionChanged += ChargingGridCollectionChanged;
                _simulator.Recorder.DischargingGrid.CollectionChanged += DischargingGridCollectionChanged;
                _simulator.TimeProvider.PropertyChanged += TimeProvider_PropertyChanged;
                _simulator.PnlManager.AccProfit.CollectionChanged += AccProfit_CollectionChanged;
                _simulator.Planner.PlannedSoC.CollectionChanged += PlannedSoC_CollectionChanged;
            }
            else
            {
                _simulator.Recorder.PropertyChanged -= Recorder_PropertyChanged;
                _simulator.Recorder.SoC.CollectionChanged -= SoC_CollectionChanged;
                _simulator.Recorder.ChargingGrid.CollectionChanged -= ChargingGridCollectionChanged;
                _simulator.Recorder.DischargingGrid.CollectionChanged -= DischargingGridCollectionChanged;
                _simulator.TimeProvider.PropertyChanged -= TimeProvider_PropertyChanged;
                _simulator.PnlManager.AccProfit.CollectionChanged -= AccProfit_CollectionChanged;
            }
        }

        private void PlannedSoC_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ObservableTimeSeries? series = sender as ObservableTimeSeries;

            DateTime t = _simulationTime;
            _SoCPlanValues.Clear();
            DateTime toDate = series.Series.GetLastTimePoint();
            while (t < toDate)
            {
                _SoCPlanValues.Add(new DateTimePoint(t,100*series[t.ToUniversalTime()]));
                t += TimeSpan.FromMinutes(15);
            }
        }

        private void DischargingGridCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Update(sender, e, _dischargeValues);
        }

        private void ChargingGridCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Update(sender, e, _chargeValues);
        }

        private void Recorder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            BatteryDataRecorder? recorder = sender as BatteryDataRecorder;
            if (recorder != null)
            {
                EquivalentCycles = recorder.EquivalentCycles[_simulationTime].ToString("f2");
            }
        }

        private void TimeProvider_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            SimulationTime = _simulator.TimeProvider.GetTime();
        }

        
        public string? CurrentPnL
        {
            get => _currentPnL; 
            private set => this.RaiseAndSetIfChanged(ref _currentPnL, value); 
        
        }

        public string? EquivalentCycles
        {
            get => _equivalentCycles; // todo
            private set => this.RaiseAndSetIfChanged(ref _equivalentCycles, value); 
        
        }

        public DateTime SimulationTime
        {
            get => _simulationTime;
            private set => this.RaiseAndSetIfChanged(ref _simulationTime, value); 
        }

        public ObservableCollection<ISeries> PnlSeries => _pnlSeries;

        public ObservableCollection<ISeries> SoC => _socSeries;

        public ObservableCollection<ISeries> Charging => _chargingSeries;

        public async Task StartSimulation()
        {
            await Task.Run(ReadData);
        }

        public async Task ReStartSimulation()
        {
            _simulator?.SetUp(_nHours , 300);
            
            ManageHandlers();

            _SoCvalues.Clear();
            _chargeValues.Clear();
            _dischargeValues.Clear();
            _pnlValues.Clear();
            _priceForecast.Clear();

            await Task.Run(ReadData);
        }

        private async Task ReadData()
        {
            _simulator.SimulationEnabled = true;
            _simulator.IsRealTime = false;
            SetPrice(_simulator.Start, _simulator.Start + TimeSpan.FromHours(_nHours));
            timeAxis[0].MaxLimit = (_simulator.Start + _visibleHorizon).Ticks;
            timeAxis[0].MinLimit = _simulator.Start.Ticks;
            timeAxis[0].ForceStepToMin = false;
            timeAxis[0].MinStep = TimeSpan.FromSeconds(60).Ticks;
           
            await Task.Run(_simulator.Simulate);

            // Simulator finished
            _simulator.SimulationEnabled = false;
        }

        private void SetPrice(DateTime fromTime, DateTime toTime)
        {
            DateTime t = fromTime;
            _priceForecast.Clear();
            while (t < toTime)
            {
                _priceForecast.Add(new DateTimePoint(t.ToLocalTime(), _simulator.PriceForecaster.PriceForecast[t.ToUniversalTime()]));
                t += TimeSpan.FromMinutes(15);
            }
        }


        public void StopSimulation()
        {
            if (_simulator != null) _simulator.SimulationEnabled = false;
        }

        private void SoC_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var series = sender as ObservableTimeSeries;
            
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (KeyValuePair<DateTime,double> datapoint in e.NewItems.Cast<KeyValuePair<DateTime,double>>())
                {
                    _SoCvalues.Add(new DateTimePoint(datapoint.Key.ToLocalTime(), datapoint.Value));
                }

                if (_SoCvalues.Count > _maxItems)
                {
                    _SoCvalues.RemoveAt(0);

                    timeAxis[0].MinLimit = _SoCvalues.First().DateTime.Ticks;
                    timeAxis[0].MaxLimit = _SoCvalues.Last().DateTime.Add(TimeSpan.FromHours(4)).Ticks;
                }
                
            }

        }

        private void AccProfit_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Update(sender, e, _pnlValues);

            double val = 0.0;
            if (_pnlValues.Any())
            {
                val = _pnlValues[_pnlValues.Count - 1].Value.Value;
            }
            CurrentPnL = (new MonetaryAmount(val, Currencies.Euro)).ToString("f0"); 
        }

        private void Update(object? sender, NotifyCollectionChangedEventArgs e, ObservableCollection<DateTimePoint> values)
        {
            var series = sender as ObservableTimeSeries;
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (KeyValuePair<DateTime,double> datapoint in e.NewItems.Cast<KeyValuePair<DateTime,double>>())
                {
                    values.Add(new DateTimePoint(datapoint.Key.ToLocalTime(), datapoint.Value));
                }

                if (values.Count > _maxItems)
                {
                    values.RemoveAt(0);
                }
                
            }
        }


        private static string Formatter(DateTime date)
        {
            return date.ToShortTimeString();
        }

        
        public Axis[] timeAxis { get; set; }
            =
            [
                new DateTimeAxis(TimeSpan.FromSeconds(300),Formatter)
                {
                  ShowSeparatorLines = true,
                  TicksAtCenter = false,
                  SeparatorsAtCenter = false,
                  MinLimit = new DateTime(2025,1,20,0,0,0).Ticks,
                  MaxLimit = (new DateTime(2025,1,20,0,0,0) + TimeSpan.FromHours(12)).Ticks,
                  MinStep = TimeSpan.FromMinutes(5).Ticks,
                  TextSize = 16
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
                    TextSize = 16,

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
                    TextSize = 16,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) 
                    { 
                        StrokeThickness = 2, 
                        PathEffect = new DashEffect(new float[] { 3, 3 }) 
                    } 
                },
                new Axis
                {
                    Name = "Price (€/MWh)",
                    TextSize = 16,
                    Position = AxisPosition.End,
                }
            ];

        public Axis[] pnlAxes { get; set; }
            = new Axis[]
            {
                new Axis
                {
                    Name = "PnL (€)",
                    
                    LabelsPaint = new SolidColorPaint(SKColors.Green), 
                    TextSize = 16,

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

        public string TimeDelay
        {
            get => _simulator.SleepTime.TotalMilliseconds.ToString();
            set
            {
                int nMilleSecs = 100;
                bool ok = int.TryParse(value, out nMilleSecs);
                if (ok)
                {
                    _simulator.SleepTime = TimeSpan.FromMilliseconds(nMilleSecs);
                }
            }
        }

        

       
    }
}
