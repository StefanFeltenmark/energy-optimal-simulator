using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Metadata;
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
        private ObservableCollection<ISeries> _soc;
        
        public object Sync { get; } = new object();

        public MainWindowViewModel()
        {
            _simulator = new BatterySimulator.BatterySimulator();
            _simulator.SetUp(24,60);
            
            _soc = new ObservableCollection<ISeries>();
            _soc.Add(new LineSeries<double>
            {
                Values = _simulator.Recorder.EnergyContent,
                Name = "SoC",
                Stroke = new SolidColorPaint(SKColors.Blue) { StrokeThickness = 4 }, 
                Fill = null,
                GeometryFill = null,
                GeometryStroke = null
            });
        }

        public Axis[] XAxes { get; set; }
            = new Axis[]
            {
                new Axis
                {
                    Name = "Time",
                    NamePaint = new SolidColorPaint(SKColors.Black), 

                    LabelsPaint = new SolidColorPaint(SKColors.Blue), 
                    TextSize = 10,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) { StrokeThickness = 2 }  
                }
            };

        public Axis[] YAxes { get; set; }
            = new Axis[]
            {
                new Axis
                {
                    Name = "SoC (%)",
                    NamePaint = new SolidColorPaint(SKColors.Red), 
                    MinLimit = 0, MaxLimit = 100,

                    LabelsPaint = new SolidColorPaint(SKColors.Green), 
                    TextSize = 20,

                    SeparatorsPaint = new SolidColorPaint(SKColors.LightSlateGray) 
                    { 
                        StrokeThickness = 2, 
                        PathEffect = new DashEffect(new float[] { 3, 3 }) 
                    } 
                }
            };
        

        public ObservableCollection<ISeries> SoC => _soc;

        public async Task StartSimulation()
        {
            _ = Task.Run(ReadData);
            //Random r = new Random();
            //for (int i = 0; i < 100; i++)
            //{
            //    _values.Add(new  DateTimePoint(DateTime.Now,r.Next(1,40)));
            //    Thread.Sleep(100);
            //}

        }

        private async Task ReadData()
        {
            // to keep this sample simple, we run the next infinite loop 
            // in a real application you should stop the loop/task when the view is disposed 
           // Random _random = new Random();

            //while (true)
            //{
            //    await Task.Delay(1000);

                // Because we are updating the chart from a different thread 
                // we need to use a lock to access the chart data. 
                // this is not necessary if your changes are made in the UI thread. 
                //lock (Sync)
                //{
                    _simulator.SimulationEnabled = true;
                    _simulator.IsRealTime = false;
                    _ = Task.Run(_simulator.Simulate);
                  //  _simulator.Recorder.EnergyContent.Add(DateTime.Now, _random.Next(0, 10));
                    //_values.Add(new DateTimePoint();
                    //if (_values.Count > 250) _values.RemoveAt(0);

                    // we need to update the separators every time we add a new point 
                    //_customAxis.CustomSeparators = GetSeparators();
             //   }
          //  }
            
        }
    }
}
