using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace BatterySimulatorGUI.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
#pragma warning disable CA1822 // Mark members as static
        
#pragma warning restore CA1822 // Mark members as static
        public ISeries[] Series { get; set; } 
            = new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = new double[] { 2, 1, 3, 5, 3, 4, 6 },
                    Fill = null
                }
            };

        private void Button_OnClick(object? sender, RoutedEventArgs e)
        {
            //message.Text = "Button clicked!";   
        }

        public void ClickHandler(object sender, RoutedEventArgs args)
        {
         
        }

        public void UpdateSeries()
        {

        }
    }
}
