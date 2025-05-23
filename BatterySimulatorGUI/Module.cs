﻿using BatteryPomaPlanner;
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
            collection.AddSingleton<IBatteryPlanner, PomaPlanner>();
            collection.AddSingleton<BatterySimulator.BatterySimulator>();
            collection.AddSingleton<Battery>();
            collection.AddTransient <MainWindowViewModel> ();
        }
    }
}
