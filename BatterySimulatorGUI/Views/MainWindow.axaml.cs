using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using BatterySimulatorGUI.ViewModels;

namespace BatterySimulatorGUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }


        private async void Button_OnClick_Run(object? sender, RoutedEventArgs e)
        {
            var vm = (MainWindowViewModel)DataContext;
            var button = (ToggleButton)sender;
            if (button.IsChecked.HasValue && button.IsChecked.Value)
            {
                button.Content = "Stop";
                await RunGuarded(vm, vm.StartSimulation, button, "Start simulation");
            }
            else
            {
                vm.StopSimulation();
                button.Content = "Start simulation";
            }
        }

        private async void Button_Restart(object? sender, RoutedEventArgs e)
        {
            var vm = (MainWindowViewModel)DataContext;
            var button = (ToggleButton)sender;

            if (button.IsChecked.HasValue && button.IsChecked.Value)
            {
                button.Content = "Stop";
                await RunGuarded(vm, vm.ReStartSimulation, button, "Restart");
            }
            else
            {
                vm.StopSimulation();
                button.Content = "Restart";
            }
        }

        /// <summary>
        /// Awaits a simulation run so any failure surfaces instead of becoming an
        /// unobserved task exception, reporting it on the view model and resetting
        /// the toggle button.
        /// </summary>
        private async Task RunGuarded(MainWindowViewModel vm, Func<Task> run, ToggleButton button, string idleLabel)
        {
            try
            {
                await run();
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"Simulation failed: {ex.Message}";
            }
            finally
            {
                button.IsChecked = false;
                button.Content = idleLabel;
                // A finished or stopped run is not paused; the next one starts running.
                PauseButton.IsChecked = false;
                PauseButton.Content = "Pause";
            }
        }

        private void Button_Pause(object? sender, RoutedEventArgs e)
        {
            var vm = (MainWindowViewModel)DataContext;
            var button = (ToggleButton)sender;
            if (button.IsChecked == true)
            {
                vm.PauseSimulation();
                button.Content = "Resume";
            }
            else
            {
                vm.ResumeSimulation();
                button.Content = "Pause";
            }
        }
    }
}