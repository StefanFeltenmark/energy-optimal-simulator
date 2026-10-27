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
    // The parameter panel.
    public partial class MainWindowViewModel
    {
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

        /// <summary>The price file's date range, for the start date picker.</summary>
        public DateTime PriceDataStart { get; private set; }

        public DateTime PriceDataEnd { get; private set; }

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

        public decimal? ProductNoiseCorrelation
        {
            get => (decimal)_settings.ProductNoiseCorrelation;
            set { if (value is { } v) { _settings.ProductNoiseCorrelation = (double)v; OnSettingChanged(); } }
        }

        public decimal? VolatilityAtDeliveryFactor
        {
            get => (decimal)_settings.VolatilityAtDeliveryFactor;
            set { if (value is { } v) { _settings.VolatilityAtDeliveryFactor = (double)v; OnSettingChanged(); } }
        }

        public decimal? VolatilityBuildUpHours
        {
            get => (decimal)_settings.VolatilityBuildUpHours;
            set { if (value is { } v) { _settings.VolatilityBuildUpHours = (double)v; OnSettingChanged(); } }
        }

        public decimal? OpeningCommonStd
        {
            get => (decimal)_settings.OpeningCommonStd;
            set { if (value is { } v) { _settings.OpeningCommonStd = (double)v; OnSettingChanged(); } }
        }

        public decimal? OpeningProductStd
        {
            get => (decimal)_settings.OpeningProductStd;
            set { if (value is { } v) { _settings.OpeningProductStd = (double)v; OnSettingChanged(); } }
        }

        public decimal? OpeningProductCorrelation
        {
            get => (decimal)_settings.OpeningProductCorrelation;
            set { if (value is { } v) { _settings.OpeningProductCorrelation = (double)v; OnSettingChanged(); } }
        }

        public decimal? OpeningQuarterShare
        {
            get => (decimal)_settings.OpeningQuarterShare;
            set { if (value is { } v) { _settings.OpeningQuarterShare = (double)v; OnSettingChanged(); } }
        }

        public bool BookAwarePlanning
        {
            get => _settings.BookAwarePlanning;
            set { _settings.BookAwarePlanning = value; OnSettingChanged(); }
        }

        public bool DayAheadMarket
        {
            get => _settings.DayAheadMarket;
            set { _settings.DayAheadMarket = value; OnSettingChanged(); }
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

        // Battery wear
        public bool BatteryWear
        {
            get => _settings.BatteryWear;
            set { _settings.BatteryWear = value; OnSettingChanged(); }
        }

        public bool WearAwarePlanning
        {
            get => _settings.WearAwarePlanning;
            set { _settings.WearAwarePlanning = value; OnSettingChanged(); }
        }

        public decimal? WearCycleLife
        {
            get => (decimal)_settings.WearCycleLife;
            set { if (value is { } v) { _settings.WearCycleLife = (double)v; OnSettingChanged(); } }
        }

        public decimal? WearEndOfLifeCapacityPercent
        {
            get => (decimal)_settings.WearEndOfLifeCapacityPercent;
            set { if (value is { } v) { _settings.WearEndOfLifeCapacityPercent = (double)v; OnSettingChanged(); } }
        }

        public decimal? WearReplacementCostPerMWh
        {
            get => (decimal)_settings.WearReplacementCostPerMWh;
            set { if (value is { } v) { _settings.WearReplacementCostPerMWh = (double)v; OnSettingChanged(); } }
        }

        public decimal? WearExtremeSoCFactor
        {
            get => (decimal)_settings.WearExtremeSoCFactor;
            set { if (value is { } v) { _settings.WearExtremeSoCFactor = (double)v; OnSettingChanged(); } }
        }

        public decimal? WearCalendarLossAtFullPercentPerYear
        {
            get => (decimal)_settings.WearCalendarLossAtFullPercentPerYear;
            set { if (value is { } v) { _settings.WearCalendarLossAtFullPercentPerYear = (double)v; OnSettingChanged(); } }
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
