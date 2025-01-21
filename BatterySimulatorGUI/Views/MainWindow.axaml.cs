using System.Diagnostics;
using Avalonia.Controls;
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
            vm.StartSimulation();
        }
    }
}