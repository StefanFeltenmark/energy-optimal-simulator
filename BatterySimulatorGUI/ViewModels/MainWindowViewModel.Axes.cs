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
    // Chart axes.
    public partial class MainWindowViewModel
    {
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

        /// <summary>Delivery-time axes for the order book and positions panels (their own; the other charts share timeAxis).</summary>
        public Axis[] bookTimeAxis { get; set; } = [DeliveryAxis()];

        public Axis[] positionTimeAxis { get; set; } = [DeliveryAxis()];

        private static Axis DeliveryAxis() =>
            new DateTimeAxis(TimeSpan.FromMinutes(SimulationSettings.ProductMinutes), date => date.ToString("ddd HH:mm"))
            {
                Name = "Delivery",
                NameTextSize = 14,
                ShowSeparatorLines = true,
                TicksAtCenter = false,
                SeparatorsAtCenter = false,
                MinStep = TimeSpan.FromMinutes(SimulationSettings.ProductMinutes).Ticks,
                TextSize = 16
            };

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
    }
}
