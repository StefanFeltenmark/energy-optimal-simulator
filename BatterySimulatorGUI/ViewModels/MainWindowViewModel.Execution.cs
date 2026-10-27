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
    // The positions and order book panels, fed by BatterySimulator.StepCompleted.
    public partial class MainWindowViewModel
    {
        private void Simulator_StepCompleted(object? sender, ExecutionSnapshot snapshot)
        {
            OnUiThread(() =>
            {
                // Positions over delivery time on a fixed axis covering the current and next delivery day, like the
                // order book; history is kept back to the start of the day and a vertical line marks the current time.
                DateTime today = snapshot.IntervalEnd.Date;
                _deliveredValues.Add(new DateTimePoint(snapshot.IntervalStart, snapshot.DeliveredMW));
                DropBefore(_deliveredValues, today);

                if (snapshot.Products.Count > 0)
                {
                    ReplaceFromFirstProduct(_plannedPowerValues, snapshot.Products, p => p.PlannedMW, today);
                    if (snapshot.HasOrderBook)
                        ReplaceFromFirstProduct(_positionValues, snapshot.Products, p => p.PositionMW, today);
                    if (snapshot.HasDayAhead)
                        ReplaceFromFirstProduct(_dayAheadValues, snapshot.Products, p => p.DayAheadMW, today);
                }
                ShowDeliveryDays(positionTimeAxis, _positionNowLine, today, snapshot.IntervalEnd);

                if (snapshot.Quotes.Count > 0)
                {
                    // The intraday curve as it looks now against delivery time, on a fixed axis covering the
                    // current and next delivery day; a vertical line marks the current time.
                    SetCurve(_askValues, snapshot.Quotes, q => q.BestAsk);
                    SetCurve(_bidValues, snapshot.Quotes, q => q.BestBid);
                    SetCurve(_midValues, snapshot.Quotes, q => q.Mid);
                    SetCurve(_bookReferenceValues, snapshot.Quotes, q => q.ReferencePrice);
                    ShowDeliveryDays(bookTimeAxis, _bookNowLine, today, snapshot.IntervalEnd);
                }

                ExecutionSummary = !snapshot.HasOrderBook ? null
                    : snapshot.HasDayAhead
                        // Profit = day-ahead + intraday - imbalance cost.
                        ? $"Day-ahead {snapshot.DayAheadNetCash:+0;-0;0} €  Intraday {snapshot.IntradayNetCash:+0;-0;0} € ({snapshot.GrossTradedMWh:F1} MWh, {snapshot.TradeCount} trades)  " +
                          $"Imbalance {snapshot.ShortImbalanceMWh + snapshot.LongImbalanceMWh:F1} MWh, {-snapshot.ImbalanceNetCost:+0;-0;0} €"
                        : $"Traded {snapshot.GrossTradedMWh:F1} MWh ({snapshot.TradeCount} trades)  Imbalance {snapshot.ShortImbalanceMWh + snapshot.LongImbalanceMWh:F1} MWh, {snapshot.ImbalanceNetCost:F0} €";
            });
        }

        /// <summary>
        /// Keeps the history before the first product (frozen positions / last plan for delivered products) and
        /// overwrites everything from it onwards with the snapshot, closing the last step at its product end.
        /// Existing points are updated in place rather than removed and re-added, so the chart does not
        /// re-draw (and animate) the whole line every step. History before <paramref name="keepFrom"/> is dropped.
        /// </summary>
        private static void ReplaceFromFirstProduct(ObservableCollection<DateTimePoint> values, IReadOnlyList<ProductSnapshot> products, Func<ProductSnapshot, double> select, DateTime keepFrom)
        {
            DateTime from = products[0].Product;
            int i = values.Count;
            while (i > 0 && values[i - 1].DateTime >= from) i--;

            void Set(DateTime time, double price)
            {
                // NaN (e.g. no plan beyond the horizon) leaves a gap.
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

            double previous = double.NaN;
            foreach (ProductSnapshot product in products)
            {
                double value = select(product);
                // Close the last step before a gap, so the product before it keeps its full width.
                if (double.IsNaN(value) && !double.IsNaN(previous)) Set(product.Product, previous);
                Set(product.Product, value);
                previous = value;
            }
            Set(products[^1].Product.AddMinutes(SimulationSettings.ProductMinutes), previous);
            while (values.Count > i) values.RemoveAt(values.Count - 1);

            DropBefore(values, keepFrom);
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

        /// <summary>Vertical lines at the current trading time in the order book and positions panels.</summary>
        private readonly RectangularSection _bookNowLine = NowLine();
        private readonly RectangularSection _positionNowLine = NowLine();

        private static RectangularSection NowLine() => new()
        {
            Stroke = new SolidColorPaint(SKColors.Black) { StrokeThickness = 2 },
            IsVisible = false
        };

        public RectangularSection[] BookSections => [_bookNowLine];

        public RectangularSection[] PositionSections => [_positionNowLine];

        /// <summary>
        /// Fixes <paramref name="axis"/> to the current and next delivery day (it moves only at midnight) and
        /// moves <paramref name="nowLine"/> to the current time.
        /// </summary>
        private static void ShowDeliveryDays(Axis[] axis, RectangularSection nowLine, DateTime today, DateTime now)
        {
            if (axis[0].MinLimit != today.Ticks)
            {
                axis[0].MinLimit = today.Ticks;
                axis[0].MaxLimit = today.AddDays(2).Ticks;
            }
            nowLine.Xi = nowLine.Xj = now.Ticks;
            nowLine.IsVisible = true;
        }

        public ObservableCollection<ISeries> OrderBook => _bookSeries;

        /// <summary>Execution totals for the header; null with the single-price market model.</summary>
        public string? ExecutionSummary
        {
            get => _executionSummary;
            private set => this.RaiseAndSetIfChanged(ref _executionSummary, value);
        }
    }
}
