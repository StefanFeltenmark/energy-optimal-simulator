using BatteryPomaPlanner;
using BatterySimulator;
using BatterySimulator.Interfaces;
using BatterySimulatorGUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;

namespace BatterySimulatorGUI
{
    public static class Module
    {
        public static void AddCommonServices(this IServiceCollection collection)
        {
            // Default planner runs standalone (no external service). The GUI can
            // swap to POMA at runtime via the Algorithm selector (see MainWindowViewModel).
            collection.AddSingleton<IBatteryPlanner, PriceLevelPlanner>();
            collection.AddSingleton<BatterySimulator.BatterySimulator>();
            collection.AddSingleton<Battery>();
            collection.AddTransient <MainWindowViewModel> ();
        }
    }
}
