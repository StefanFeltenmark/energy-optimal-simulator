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
        private readonly SimulationSettings _settings = new SimulationSettings { DurationHours = 1000, TimeStepSeconds = 300 };
        private IReadOnlyList<string> _priceAreas = Array.Empty<string>();

        public MainWindowViewModel()
        {
            
        }
        
        public MainWindowViewModel(BatterySimulator.BatterySimulator simulator)
        {
            _simulator = simulator;

            _priceAreas = BatterySimulator.BatterySimulator.ReadPriceAreas(_settings.PriceFile);
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

        private void Simulator_StepCompleted(object? sender, ExecutionSnapshot snapshot)
        {
            OnUiThread(() =>
            {
                _deliveredValues.Add(new DateTimePoint(snapshot.IntervalStart, snapshot.DeliveredMW));
                DropBefore(_deliveredValues, snapshot.IntervalStart - _visibleHorizon);

                if (snapshot.Products.Count > 0)
                {
                    ReplaceFromFirstProduct(_plannedPowerValues, snapshot.Products, p => p.PlannedMW);
                    if (snapshot.HasOrderBook)
                        ReplaceFromFirstProduct(_positionValues, snapshot.Products, p => p.PositionMW);
                }

                if (snapshot.Quotes.Count > 0)
                {
                    // The whole intraday curve as it looks now: every open product against delivery time.
                    SetCurve(_askValues, snapshot.Quotes, q => q.BestAsk);
                    SetCurve(_bidValues, snapshot.Quotes, q => q.BestBid);
                    SetCurve(_midValues, snapshot.Quotes, q => q.Mid);
                    SetCurve(_bookReferenceValues, snapshot.Quotes, q => q.ReferencePrice);
                    bookTimeAxis[0].MinLimit = snapshot.Quotes[0].Product.Ticks;
                    bookTimeAxis[0].MaxLimit = snapshot.Quotes[^1].Product.AddMinutes(SimulationSettings.ProductMinutes).Ticks;
                }

                ExecutionSummary = snapshot.HasOrderBook
                    ? $"Traded {snapshot.GrossTradedMWh:F1} MWh ({snapshot.TradeCount} trades)  Imbalance {snapshot.ShortImbalanceMWh + snapshot.LongImbalanceMWh:F1} MWh, {snapshot.ImbalanceNetCost:F0} €"
                    : null;
            });
        }

        /// <summary>
        /// Keeps the history before the first product (frozen positions / last plan for delivered products) and
        /// overwrites everything from it onwards with the snapshot, closing the last step at its product end.
        /// Existing points are updated in place rather than removed and re-added, so the chart does not
        /// re-draw (and animate) the whole line every step.
        /// </summary>
        private void ReplaceFromFirstProduct(ObservableCollection<DateTimePoint> values, IReadOnlyList<ProductSnapshot> products, Func<ProductSnapshot, double> select)
        {
            DateTime from = products[0].Product;
            int i = values.Count;
            while (i > 0 && values[i - 1].DateTime >= from) i--;

            void Set(DateTime time, double value)
            {
                if (i < values.Count)
                {
                    DateTimePoint point = values[i];
                    if (point.DateTime != time) point.DateTime = time;
                    if (point.Value != value) point.Value = value;
                }
                else
                {
                    values.Add(new DateTimePoint(time, value));
                }
                i++;
            }

            foreach (ProductSnapshot product in products) Set(product.Product, select(product));
            Set(products[^1].Product.AddMinutes(SimulationSettings.ProductMinutes), select(products[^1]));
            while (values.Count > i) values.RemoveAt(values.Count - 1);

            DropBefore(values, from - _visibleHorizon);
        }

        private static void DropBefore(ObservableCollection<DateTimePoint> values, DateTime cutoff)
        {
            while (values.Count > 0 && values[0].DateTime < cutoff) values.RemoveAt(0);
        }

        private static StepLineSeries<DateTimePoint> BookLine(ObservableCollection<DateTimePoint> values, string name, SKColor color, float thickness, bool dashed = false)
        {
            var stroke = new SolidColorPaint(color) { StrokeThickness = thickness };
            if (dashed) stroke.PathEffect = new DashEffect(new float[] { 4, 3 });
            return new StepLineSeries<DateTimePoint>
            {
                Values = values,
                Name = name,
                Stroke = stroke,
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null
            };
        }

        /// <summary>
        /// Rewrites a curve over delivery time (one step per product, closed at the last product's end),
        /// updating existing points in place.
        /// </summary>
        private static void SetCurve(ObservableCollection<DateTimePoint> values, IReadOnlyList<BookQuote> quotes, Func<BookQuote, double> select)
        {
            int i = 0;
            void Set(DateTime time, double price)
            {
                // An empty book side has no price; leave a gap rather than a misleading value.
                double? value = double.IsNaN(price) ? null : price;
                if (i < values.Count)
                {
                    DateTimePoint point = values[i];
                    if (point.DateTime != time) point.DateTime = time;
                    if (point.Value != value) point.Value = value;
                }
                else
                {
                    values.Add(new DateTimePoint(time, value));
                }
                i++;
            }

            foreach (BookQuote quote in quotes) Set(quote.Product, select(quote));
            Set(quotes[^1].Product.AddMinutes(SimulationSettings.ProductMinutes), select(quotes[^1]));
            while (values.Count > i) values.RemoveAt(values.Count - 1);
        }

        public ObservableCollection<ISeries> Positions => _positionSeries;

        public ObservableCollection<ISeries> OrderBook => _bookSeries;

        /// <summary>Execution totals for the header; null with the single-price market model.</summary>
        public string? ExecutionSummary
        {
            get => _executionSummary;
            private set => this.RaiseAndSetIfChanged(ref _executionSummary, value);
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
            _askValues.Clear();
            _bidValues.Clear();
            _midValues.Clear();
            _bookReferenceValues.Clear();
            ExecutionSummary = null;
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

        public Axis[] positionAxes { get; set; }
            = [
                new Axis
                {
                    Name = "Position (MW)",

                    LabelsPaint = new SolidColorPaint(SKColors.Green),
                    TextSize = 16,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray)
                    {
                        StrokeThickness = 2,
                        PathEffect = new DashEffect(new float[] { 3, 3 })
                    }
                }
            ];

        /// <summary>Delivery-time axis for the order book panel (its own; the other charts share timeAxis).</summary>
        public Axis[] bookTimeAxis { get; set; }
            =
            [
                new DateTimeAxis(TimeSpan.FromMinutes(SimulationSettings.ProductMinutes), Formatter)
                {
                    Name = "Delivery",
                    NameTextSize = 14,
                    ShowSeparatorLines = true,
                    TicksAtCenter = false,
                    SeparatorsAtCenter = false,
                    MinStep = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes).Ticks,
                    TextSize = 16
                }
            ];

        public Axis[] bookAxes { get; set; }
            = [
                new Axis
                {
                    Name = "€/MWh",

                    LabelsPaint = new SolidColorPaint(SKColors.Green),
                    TextSize = 16,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray)
                    {
                        StrokeThickness = 2,
                        PathEffect = new DashEffect(new float[] { 3, 3 })
                    }
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

        // ---- Parameter panel -------------------------------------------------------------
        // Bound to NumericUpDown (decimal?) controls; a cleared box (null) keeps the old value.
        // The values are stored in _settings, which the simulator holds by reference: planner settings
        // are picked up at the next re-plan of a running simulation, the rest on the next Start/Restart.

        private const string InvalidSettingsPrefix = "Invalid settings: ";

        private void OnSettingChanged()
        {
            var errors = _settings.Validate();
            if (errors.Count > 0)
                StatusMessage = InvalidSettingsPrefix + string.Join(" ", errors);
            else if (StatusMessage?.StartsWith(InvalidSettingsPrefix) == true)
                StatusMessage = null;
        }

        // Simulation
        public DateTime? StartDate
        {
            get => _settings.Start.Date;
            set { if (value is { } d) { _settings.Start = d.Date + _settings.Start.TimeOfDay; OnSettingChanged(); } }
        }

        public decimal? StartHour
        {
            get => _settings.Start.Hour;
            set { if (value is { } h) { _settings.Start = _settings.Start.Date.AddHours((int)h); OnSettingChanged(); } }
        }

        public decimal? DurationHours
        {
            get => _settings.DurationHours;
            set { if (value is { } v) { _settings.DurationHours = (int)v; OnSettingChanged(); } }
        }

        public decimal? TimeStepSeconds
        {
            get => _settings.TimeStepSeconds;
            set { if (value is { } v) { _settings.TimeStepSeconds = (int)v; OnSettingChanged(); } }
        }

        public decimal? DelayMilliseconds
        {
            get => _settings.SleepMilliseconds;
            set
            {
                if (value is not { } v) return;
                _settings.SleepMilliseconds = (int)v;
                // Pacing can be changed while a run is in progress.
                if (_simulator != null) _simulator.SleepTime = TimeSpan.FromMilliseconds(_settings.SleepMilliseconds);
                OnSettingChanged();
            }
        }

        public decimal? PlanningHorizonHours
        {
            get => (decimal)_settings.PlanningHorizonHours;
            set { if (value is { } v) { _settings.PlanningHorizonHours = (double)v; OnSettingChanged(); } }
        }

        public decimal? ReplanIntervalMinutes
        {
            get => (decimal)_settings.ReplanIntervalMinutes;
            set { if (value is { } v) { _settings.ReplanIntervalMinutes = (double)v; OnSettingChanged(); } }
        }

        // Market
        public IReadOnlyList<string> PriceAreas => _priceAreas;

        public string? SelectedPriceArea
        {
            get => _settings.PriceArea;
            set { if (value != null) { _settings.PriceArea = value; OnSettingChanged(); } }
        }

        public decimal? BuySellSpread
        {
            get => (decimal)_settings.BuySellSpread;
            set { if (value is { } v) { _settings.BuySellSpread = (double)v; OnSettingChanged(); } }
        }

        /// <summary>Index into the market model selector: 0 = single price, 1 = order book.</summary>
        public int MarketModelIndex
        {
            get => (int)_settings.MarketModel;
            set { _settings.MarketModel = (MarketModel)value; OnSettingChanged(); }
        }

        // Order book
        public decimal? GateClosureMinutes
        {
            get => (decimal)_settings.GateClosureMinutes;
            set { if (value is { } v) { _settings.GateClosureMinutes = (double)v; OnSettingChanged(); } }
        }

        public decimal? MidVolatility
        {
            get => (decimal)_settings.MidVolatility;
            set { if (value is { } v) { _settings.MidVolatility = (double)v; OnSettingChanged(); } }
        }

        public decimal? ProductNoiseVolatility
        {
            get => (decimal)_settings.ProductNoiseVolatility;
            set { if (value is { } v) { _settings.ProductNoiseVolatility = (double)v; OnSettingChanged(); } }
        }

        public bool BookAwarePlanning
        {
            get => _settings.BookAwarePlanning;
            set { _settings.BookAwarePlanning = value; OnSettingChanged(); }
        }

        public decimal? SpreadNearDelivery
        {
            get => (decimal)_settings.SpreadNearDelivery;
            set { if (value is { } v) { _settings.SpreadNearDelivery = (double)v; OnSettingChanged(); } }
        }

        public decimal? SpreadFarFromDelivery
        {
            get => (decimal)_settings.SpreadFarFromDelivery;
            set { if (value is { } v) { _settings.SpreadFarFromDelivery = (double)v; OnSettingChanged(); } }
        }

        public decimal? LiquidityBuildUpHours
        {
            get => (decimal)_settings.LiquidityBuildUpHours;
            set { if (value is { } v) { _settings.LiquidityBuildUpHours = (double)v; OnSettingChanged(); } }
        }

        public decimal? BookLevels
        {
            get => _settings.BookLevels;
            set { if (value is { } v) { _settings.BookLevels = (int)v; OnSettingChanged(); } }
        }

        public decimal? LevelPriceStep
        {
            get => (decimal)_settings.LevelPriceStep;
            set { if (value is { } v) { _settings.LevelPriceStep = (double)v; OnSettingChanged(); } }
        }

        public decimal? LevelVolumeMWh
        {
            get => (decimal)_settings.LevelVolumeMWh;
            set { if (value is { } v) { _settings.LevelVolumeMWh = (double)v; OnSettingChanged(); } }
        }

        public decimal? FarDepthPercent
        {
            get => (decimal)(_settings.FarDepthFraction * 100);
            set { if (value is { } v) { _settings.FarDepthFraction = (double)v / 100; OnSettingChanged(); } }
        }

        public decimal? LiquidityRecoveryMinutes
        {
            get => (decimal)_settings.LiquidityRecoveryMinutes;
            set { if (value is { } v) { _settings.LiquidityRecoveryMinutes = (double)v; OnSettingChanged(); } }
        }

        public decimal? ImbalancePenalty
        {
            get => (decimal)_settings.ImbalancePenalty;
            set { if (value is { } v) { _settings.ImbalancePenalty = (double)v; OnSettingChanged(); } }
        }

        public decimal? OrderBookSeed
        {
            get => _settings.OrderBookSeed;
            set { if (value is { } v) { _settings.OrderBookSeed = (int)v; OnSettingChanged(); } }
        }

        // Battery
        public decimal? PowerMW
        {
            get => (decimal)_settings.PowerMW;
            set { if (value is { } v) { _settings.PowerMW = (double)v; OnSettingChanged(); } }
        }

        public decimal? EnergyMWh
        {
            get => (decimal)_settings.EnergyMWh;
            set { if (value is { } v) { _settings.EnergyMWh = (double)v; OnSettingChanged(); } }
        }

        public decimal? ChargeEfficiencyPercent
        {
            get => (decimal)(_settings.ChargeEfficiency * 100);
            set { if (value is { } v) { _settings.ChargeEfficiency = (double)v / 100; OnSettingChanged(); } }
        }

        public decimal? DischargeEfficiencyPercent
        {
            get => (decimal)(_settings.DischargeEfficiency * 100);
            set { if (value is { } v) { _settings.DischargeEfficiency = (double)v / 100; OnSettingChanged(); } }
        }

        public decimal? InitialSoCPercent
        {
            get => (decimal)_settings.InitialSoCPercent;
            set { if (value is { } v) { _settings.InitialSoCPercent = (double)v; OnSettingChanged(); } }
        }

        // PriceLevelPlanner
        public decimal? BuyThresholdPercent
        {
            get => (decimal)_settings.BuyThresholdPercent;
            set { if (value is { } v) { _settings.BuyThresholdPercent = (double)v; OnSettingChanged(); } }
        }

        public decimal? SellThresholdPercent
        {
            get => (decimal)_settings.SellThresholdPercent;
            set { if (value is { } v) { _settings.SellThresholdPercent = (double)v; OnSettingChanged(); } }
        }

        public decimal? LookAheadHours
        {
            get => (decimal)_settings.LookAheadHours;
            set { if (value is { } v) { _settings.LookAheadHours = (double)v; OnSettingChanged(); } }
        }

        // POMA
        public decimal? SocSoftMinPercent
        {
            get => (decimal)_settings.SocSoftMinPercent;
            set { if (value is { } v) { _settings.SocSoftMinPercent = (double)v; OnSettingChanged(); } }
        }

        public decimal? SocSoftMaxPercent
        {
            get => (decimal)_settings.SocSoftMaxPercent;
            set { if (value is { } v) { _settings.SocSoftMaxPercent = (double)v; OnSettingChanged(); } }
        }

        

       
    }
}
