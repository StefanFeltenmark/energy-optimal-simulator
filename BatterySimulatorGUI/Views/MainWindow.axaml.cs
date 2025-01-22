using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using BatterySimulatorGUI.ViewModels;
using LiveChartsCore;

namespace BatterySimulatorGUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }


        private void Button_OnClick_Run(object? sender, RoutedEventArgs e)
        {
            var vm = (MainWindowViewModel)DataContext;
            var button = (ToggleButton)sender;
            //button.
            if (button.IsChecked.HasValue && button.IsChecked.Value)
            {
                vm.StartSimulation();
                button.Content = "Stop simulation";
            }
            else
            {
                vm.StopSimulation();
                button.Content = "Start simulation";
            }
        }
    }
}