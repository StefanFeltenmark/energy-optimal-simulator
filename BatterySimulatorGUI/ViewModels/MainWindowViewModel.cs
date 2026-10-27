using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BatterySimulator;
using BatterySimulator.Interfaces;
using BatteryPomaPlanner;
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
        private ObservableCollection<DateTimePoint> _netChargevalues;
        private ObservableCollection<DateTimePoint> _chargeValues;
        private ObservableCollection<DateTimePoint> _dischargeValues;
        private ObservableCollection<DateTimePoint> _pnlValues;

        // Position panel (fed by BatterySimulator.StepCompleted)
        private ObservableCollection<ISeries> _positionSeries;
        private ObservableCollection<DateTimePoint> _positionValues;
        private ObservableCollection<DateTimePoint> _plannedPowerValues;
        private ObservableCollection<DateTimePoint> _deliveredValues;
        private ObservableCollection<DateTimePoint> _dayAheadValues = new();
        private string? _executionSummary;

        // Order book panel: top of the book for the front (first tradable) product
        private ObservableCollection<ISeries> _bookSeries;
        private ObservableCollection<DateTimePoint> _askValues;
        private ObservableCollection<DateTimePoint> _bidValues;
        private ObservableCollection<DateTimePoint> _midValues;
        private ObservableCollection<DateTimePoint> _bookReferenceValues;

        private DateTime _simulationTime;
        private string? _currentPnL;
        private string? _equivalentCycles;
        private string? _statusMessage;
        private int _selectedAlgorithmIndex;
        private CancellationTokenSource? _cts;
        private TimeSpan _visibleHorizon;
        private int _maxItems = 180;

        // Edited by the parameter panel and applied on every Start/Restart.
        private readonly SimulationSettings _settings = GuiDefaults(SimulationSettings.Recent());

        private static SimulationSettings GuiDefaults(SimulationSettings settings)
        {
            settings.DurationHours = 1000;
            settings.TimeStepSeconds = 300;
            return settings;
        }
        private IReadOnlyList<string> _priceAreas = Array.Empty<string>();

        public MainWindowViewModel()
        {
            
        }
        
        public MainWindowViewModel(BatterySimulator.BatterySimulator simulator)
        {
            _simulator = simulator;

            _priceAreas = BatterySimulator.BatterySimulator.ReadPriceAreas(_settings.PriceFile);
            (PriceDataStart, PriceDataEnd) = BatterySimulator.BatterySimulator.ReadPriceRange(_settings.PriceFile);
            ApplySelectedPlanner();
            _simulator.SetUp(_settings);

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
                IsVisibleAtLegend = true,
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
                Name = "Reference price",
                Stroke = new SolidColorPaint(SKColors.LightPink) { StrokeThickness = 2 },
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

            // Position panel: what was traded, what the planner wants, and what the battery delivered.
            _positionSeries = new ObservableCollection<ISeries>();
            _positionValues = new ObservableCollection<DateTimePoint>();
            _plannedPowerValues = new ObservableCollection<DateTimePoint>();
            _deliveredValues = new ObservableCollection<DateTimePoint>();
            _positionSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _deliveredValues,
                Name = "Delivered",
                Stroke = new SolidColorPaint(SKColors.Gray) { StrokeThickness = 1 },
                Fill = new SolidColorPaint(SKColors.LightGray.WithAlpha(120)),
                GeometryFill = null,
                GeometryStroke = null
            });
            _positionSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _positionValues,
                Name = "Traded position",
                Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 3 },
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null
            });
            // With the day-ahead market: the accepted day-ahead volumes. The gap to the traded position is what
            // intraday trading changed on top of day-ahead.
            _positionSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _dayAheadValues,
                Name = "Day-ahead position",
                Stroke = new SolidColorPaint(SKColors.MediumPurple) { StrokeThickness = 2, PathEffect = new DashEffect(new float[] { 2, 3 }) },
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null
            });
            // Drawn last: the position usually equals the plan, so the dashes run along the blue line and
            // only separate from it where a fill was partial or a product had passed gate closure.
            _positionSeries.Add(new StepLineSeries<DateTimePoint>
            {
                Values = _plannedPowerValues,
                Name = "Planned",
                Stroke = new SolidColorPaint(SKColors.Orange) { StrokeThickness = 2, PathEffect = new DashEffect(new float[] { 6, 4 }) },
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null
            });
            _askValues = new ObservableCollection<DateTimePoint>();
            _bidValues = new ObservableCollection<DateTimePoint>();
            _midValues = new ObservableCollection<DateTimePoint>();
            _bookReferenceValues = new ObservableCollection<DateTimePoint>();
            _bookSeries = new ObservableCollection<ISeries>
            {
                BookLine(_bookReferenceValues, "Realized (hindsight)", SKColors.LightPink, 2),
                BookLine(_midValues, "Mid", SKColors.Gray, 1, dashed: true),
                BookLine(_askValues, "Best ask", SKColors.IndianRed, 2),
                BookLine(_bidValues, "Best bid", SKColors.SeaGreen, 2)
            };

            _simulator.StepCompleted += Simulator_StepCompleted;

            _simulationTime = _simulator.TimeProvider.GetTime();

            UpdateChargeAxisLimits();
        }

        private void UpdateChargeAxisLimits()
        {
            double power = _simulator.Battery1.InitialCapacityC.CovertToUnit(Units.MegaWatt).Value;
            netChargeAxes[0].MaxLimit = power + 1;
            netChargeAxes[0].MinLimit = -power - 1;
            positionAxes[0].MaxLimit = power + 1;
            positionAxes[0].MinLimit = -power - 1;
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
                _simulator.Planner.PlannedSoC.CollectionChanged -= PlannedSoC_CollectionChanged;
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the UI thread. The simulation raises its
        /// change notifications from a background thread, so every handler that touches
        /// bound state must be marshalled or Avalonia throws a cross-thread exception.
        /// </summary>
        private static void OnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }

        private void PlannedSoC_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ObservableTimeSeries? series = sender as ObservableTimeSeries;
            if (series == null) return;

            OnUiThread(() =>
            {
                DateTime t = _simulationTime;
                _SoCPlanValues.Clear();
                DateTime toDate = series.Series.GetLastTimePoint();
                while (t < toDate)
                {
                    _SoCPlanValues.Add(new DateTimePoint(t, series[t.ToUniversalTime()]));
                    t += TimeSpan.FromMinutes(15);
                }
            });
        }

        // Recorder values are stamped at the end of their interval; draw the charge/discharge
        // columns at the interval midpoint so they line up with the price steps.
        private TimeSpan ColumnOffset => -_simulator.Delta / 2;

        private void DischargingGridCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnUiThread(() => Update(sender, e, _dischargeValues, ColumnOffset));
        }

        private void ChargingGridCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnUiThread(() => Update(sender, e, _chargeValues, ColumnOffset));
        }

        private void Recorder_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            BatteryDataRecorder? recorder = sender as BatteryDataRecorder;
            if (recorder != null)
            {
                OnUiThread(() => EquivalentCycles = recorder.EquivalentCycles[_simulationTime].ToString("f2"));
            }
        }

        private void TimeProvider_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            OnUiThread(() => SimulationTime = _simulator.TimeProvider.GetTime());
        }

        
        public string? CurrentPnL
        {
            get => _currentPnL; // todo
            private set => this.RaiseAndSetIfChanged(ref _currentPnL, value); 
        
        }

        public string? EquivalentCycles
        {
            get => _equivalentCycles; // todo
            private set => this.RaiseAndSetIfChanged(ref _equivalentCycles, value);

        }

        private string? _wearSummary;

        /// <summary>Wear cost so far (included in the profit) and capacity lost; null without battery wear.</summary>
        public string? WearSummary
        {
            get => _wearSummary;
            private set => this.RaiseAndSetIfChanged(ref _wearSummary, value);
        }

        public DateTime SimulationTime
        {
            get => _simulationTime;
            private set => this.RaiseAndSetIfChanged(ref _simulationTime, value);
        }

        public string? StatusMessage
        {
            get => _statusMessage;
            set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
        }

        /// <summary>
        /// Index into the Algorithm selector: 0 = Price policy, 1 = POMA.
        /// The choice is applied to the simulator on the next Start/Restart.
        /// </summary>
        public int SelectedAlgorithmIndex
        {
            get => _selectedAlgorithmIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedAlgorithmIndex, value);
        }

        public ObservableCollection<ISeries> PnlSeries => _pnlSeries;

        public ObservableCollection<ISeries> SoC => _socSeries;

        public ObservableCollection<ISeries> Charging => _chargingSeries;

        public async Task StartSimulation()
        {
            InitializeSimulator();
            await Task.Run(ReadData);
        }

        public async Task ReStartSimulation()
        {
            InitializeSimulator();
            await Task.Run(ReadData);
        }

        /// <summary>
        /// (Re)wires the simulator for a fresh run: detaches the current event
        /// handlers, applies the selected planner, runs SetUp, re-attaches handlers
        /// and clears the chart series. Shared by Start and Restart.
        /// </summary>
        private void InitializeSimulator()
        {
            StatusMessage = null;

            ManageHandlers(false);
            ApplySelectedPlanner();
            _simulator.SetUp(_settings);
            ManageHandlers(true);

            // The time step and battery power may have changed: resize the sliding window and charge axis.
            _maxItems = (int) (_visibleHorizon.TotalMinutes / _simulator.Delta.TotalMinutes);
            UpdateChargeAxisLimits();

            // SetUp creates a fresh recorder, so re-apply the sliding-window cap.
            _simulator.Recorder.SoC.MaxItems = _maxItems;
            _simulator.Recorder.ChargingGrid.MaxItems = _maxItems;
            _simulator.Recorder.DischargingGrid.MaxItems = _maxItems;

            _SoCvalues.Clear();
            _SoCPlanValues.Clear();
            _chargeValues.Clear();
            _dischargeValues.Clear();
            _pnlValues.Clear();
            _priceForecast.Clear();
            _positionValues.Clear();
            _plannedPowerValues.Clear();
            _deliveredValues.Clear();
            _dayAheadValues.Clear();
            _askValues.Clear();
            _bidValues.Clear();
            _midValues.Clear();
            _bookReferenceValues.Clear();
            _bookNowLine.IsVisible = false;
            _positionNowLine.IsVisible = false;
            bookTimeAxis[0].MinLimit = bookTimeAxis[0].MaxLimit = null;
            positionTimeAxis[0].MinLimit = positionTimeAxis[0].MaxLimit = null;
            ExecutionSummary = null;
            WearSummary = null;
        }

        /// <summary>
        /// Sets the simulator's planner from <see cref="SelectedAlgorithmIndex"/>.
        /// SetUp performs the planner-specific wiring afterwards.
        /// </summary>
        private void ApplySelectedPlanner()
        {
            IBatteryPlanner planner = SelectedAlgorithmIndex switch
            {
                1 => new PomaPlanner(),
                _ => new PriceLevelPlanner()
            };
            _simulator.Planner = planner;
        }

        private async Task ReadData()
        {
            _simulator.SimulationEnabled = true;
            _simulator.IsRealTime = false;
            SetPrice(_simulator.Start, _simulator.Start + TimeSpan.FromHours(_settings.DurationHours));
            timeAxis[0].MaxLimit = (_simulator.Start + _visibleHorizon).Ticks;
            timeAxis[0].MinLimit = _simulator.Start.Ticks;
            timeAxis[0].ForceStepToMin = false;
            timeAxis[0].MinStep = TimeSpan.FromSeconds(60).Ticks;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            await Task.Run(() => _simulator.Simulate(token), token);

            // Simulator finished
            _simulator.SimulationEnabled = false;
        }

        private void SetPrice(DateTime fromTime, DateTime toTime)
        {
            DateTime t = fromTime;
            _priceForecast.Clear();
            while (t < toTime)
            {
                // The forecast is keyed by local clock time (like the CSV prices); only POMA's plan is UTC-keyed.
                _priceForecast.Add(new DateTimePoint(t.ToLocalTime(), _simulator.PriceForecaster.PriceForecast[t]));
                t += TimeSpan.FromMinutes(15);
            }
        }


        public void StopSimulation()
        {
            _cts?.Cancel();
            if (_simulator != null) _simulator.SimulationEnabled = false;
        }

        /// <summary>Holds a running simulation before its next step; settings edits and Stop still work while paused.</summary>
        public void PauseSimulation() => _simulator?.Pause();

        public void ResumeSimulation() => _simulator?.Resume();

        private void SoC_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null) return;

            OnUiThread(() =>
            {
                foreach (KeyValuePair<DateTime,double> datapoint in e.NewItems.Cast<KeyValuePair<DateTime,double>>())
                {
                    _SoCvalues.Add(new DateTimePoint(datapoint.Key.ToLocalTime(), 100.0*datapoint.Value));
                }

                if (_SoCvalues.Count > _maxItems)
                {
                    _SoCvalues.RemoveAt(0);

                    timeAxis[0].MinLimit = _SoCvalues.First().DateTime.Ticks;
                    timeAxis[0].MaxLimit = _SoCvalues.Last().DateTime.Add(TimeSpan.FromHours(4)).Ticks;
                }
            });
        }

        private void AccProfit_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnUiThread(() =>
            {
                Update(sender, e, _pnlValues);

                double val = 0.0;
                if (_pnlValues.Any())
                {
                    val = _pnlValues[_pnlValues.Count - 1].Value.Value;
                }
                CurrentPnL = (new MonetaryAmount(val, Currencies.Euro)).ToString(); // todo

                var pnl = _simulator?.PnlManager;
                WearSummary = pnl != null && _simulator!.Wear != null
                    ? $"Wear: {pnl.AccWearCost:F0} € ({pnl.CapacityLostMWh * 1000:F1} kWh lost)"
                    : null;
            });
        }

        private void Update(object? sender, NotifyCollectionChangedEventArgs e, ObservableCollection<DateTimePoint> values, TimeSpan timeOffset = default)
        {
            var series = sender as ObservableTimeSeries;
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (KeyValuePair<DateTime,double> datapoint in e.NewItems.Cast<KeyValuePair<DateTime,double>>())
                {
                    values.Add(new DateTimePoint(datapoint.Key.ToLocalTime() + timeOffset, datapoint.Value));
                }

                if (values.Count > _maxItems)
                {
                    values.RemoveAt(0);
                }
                
            }
        }


    }
}
