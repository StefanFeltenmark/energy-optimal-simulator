﻿using BatterySimulator;
using BatterySimulator.Interfaces;
using Domain;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.Common.Solution;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage.Providers;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;
using Powel.Optimal.MultiAsset.Infrastructure;

namespace BatteryPomaPlanner
{
    public class PomaPlanner : IBatteryPlanner, IObserver<BatteryState>
    {
        private ILogger _logger;
        private PomaServiceClient _multiAssetService;
        private MultiAssetData _data;
        private PomaSolutionSet? _solution;
        private TimeSeries _plan;
        private Battery _battery;
        private EnergyMarket _market;
        private Guid _optimizationId;
        private BatteryState? _currentState;
        private IDisposable unsubscriber;
        private ObservableTimeSeries _plannedSoC;
        

        public PomaPlanner()
        {
            _logger = new NullLogger();
            _multiAssetService = new PomaServiceClient(new AppSettingsProviderDummy(), _logger);
            _plan = new TimeSeries();
            _data = new MultiAssetData();
            _optimizationId = Guid.NewGuid();
            _plannedSoC = new ObservableTimeSeries();
        }

        public void SetUp(Battery battery, EnergyMarket market)
        {
            PriceUnit euroPerMw = new PriceUnit(Currencies.Euro, Units.MegaWatt);

            _battery = battery;
            _data.EnergyStorage = new EnergyStorageData
            {
                Batteries =
                [
                    battery
                ]
            };
            _data.CommonData = new CommonData();
            _market = market;

            _data.CommonData.EnergyMarkets.Add(_market);

            // Market data
            market.Ts.PowerBuyMax = new TimeSeries();
            market.Ts.PowerBuyMax.DefaultValue = 1000000;
            market.Ts.PowerBuyMin = new TimeSeries();
            market.Ts.PowerSellMin = new TimeSeries();
            market.Ts.PowerSellMax = new TimeSeries();
            market.Ts.PowerSellMax.DefaultValue = 1000000;
            market.Ts.EnergyDeficitPenaltyPrice = new TimeSeries();
            market.Ts.EnergyDeficitPenaltyPrice.DefaultValue = 1000000;
            market.Ts.EnergySurplusPenaltyPrice = new TimeSeries();
            market.Ts.EnergySurplusPenaltyPrice.DefaultValue = 1000000;

            market.Ts.PowerLoad = new TimeSeries();

            market.EnergyProviders = new List<EnergyProvider>();
            BatteryEnergyProvider provider = new BatteryEnergyProvider(market.Id, _battery.Id)
            {
                Battery = _battery,
                EnergyMarket = market,
                Availability = new TimeSeries()
            };
            provider.Availability.DefaultValue = 1;
            provider.MaxDelivery = new TimeSeries();
            provider.MaxDelivery.DefaultValue = 100;
            provider.MinDelivery = new TimeSeries();
            provider.MinDelivery.DefaultValue = -100;
            
            market.EnergyProviders.Add(provider);

            // Battery time series
            _battery.RampCost = new UnitPrice(0.1, euroPerMw);
            _battery.Ts.SocSoftMaxPenaltyPrice = new TimeSeries();
            _battery.Ts.SocSoftMinPenaltyPrice = new TimeSeries();
            _battery.Ts.SocSoftMaxPenaltyPrice.DefaultValue = 60;
            _battery.Ts.SocSoftMinPenaltyPrice.DefaultValue = 60;
            _battery.Ts.ChargeCost = new TimeSeries();
            _battery.Ts.ChargeCost.DefaultValue = 0.1;
            _battery.Ts.DischargeCost = new TimeSeries();
            _battery.Ts.DischargeCost.DefaultValue = 0.1;
            _battery.Ts.AvailabilityFlag = new TimeSeries();
            _battery.Ts.AvailabilityFlag.DefaultValue = 1;
            _battery.Ts.AvailableEnergyPercent = new TimeSeries();
            _battery.Ts.AvailableEnergyPercent.DefaultValue = 100;
            _battery.Ts.ChargeMax = new TimeSeries();
            _battery.Ts.ChargeMax.DefaultValue = _battery.NominalChargeCapacity.Value;
            _battery.Ts.DischargeMax = new TimeSeries();
            _battery.Ts.DischargeMax.DefaultValue = _battery.NominalChargeCapacity.Value;
            _battery.Ts.EnergyContentCorrection = new TimeSeries();
            _battery.Ts.MipFlag = new TimeSeries();
            _battery.Ts.SocMax = new TimeSeries();
            _battery.Ts.SocMax.DefaultValue = 1;
            _battery.Ts.SocMin = new TimeSeries();
            _battery.Ts.SoftSchedule = new TimeSeries();
            _battery.Ts.SoftScheduleFlag = new TimeSeries();
            _battery.Ts.SocSoftMax = new TimeSeries();
            _battery.Ts.SocSoftMax.DefaultValue = 0.85;
            _battery.Ts.SocSoftMin = new TimeSeries();
            _battery.Ts.SocSoftMin.DefaultValue = 0.15;
            _battery.FinalSocMax = new Percentage(100);
            _battery.FinalSocMin = new Percentage(0);
            _battery.FinalSocPenaltyPrice = new UnitPrice(1000, new PriceUnit(Currencies.Euro,  Units.MegaWattHour));
            _battery.SoftSchedulePenaltyPrice = new UnitPrice(1000, new PriceUnit(Currencies.Euro, Units.MegaWattHour));
            _battery.FinalStoragePrice = new UnitPrice(1000, new PriceUnit(Currencies.Euro,  Units.MegaWattHour));

            _data.CommonData.Parameters.CaseName = "BatterySimulation";
            _data.OptimizationId = _optimizationId;

            _data.CommonData.SolveParameters.LogLevel = 2;
        }

        public string Name => "POMA";


        public TimeSeries Plan
        {
            get => _plan;
            set => _plan = value;
        }

        public Battery Battery1
        {
            get => _battery;
            set => _battery = value;
        }

        public ObservableTimeSeries PlannedSoC
        {
            get => _plannedSoC;
            set => _plannedSoC = value;
        }

        private async Task<PomaSolutionSet> CallMultiAssetService(MultiAssetData options)
        {
            return await _multiAssetService.Run<PomaSolutionSet>("api/poma/start", options);
        }

        public Power GetPlannedProduction(DateTime time)
        {
            return new Power(_plan[time.ToUniversalTime()], Units.MegaWatt);
        }

       

        public async Task UpdatePlan(DateTime planStart, TimeSpan resolution, int nPeriods)
        {
            _data.CommonData.PlanPeriod = new PlanningPeriod(planStart, resolution, nPeriods);
            _data.CommonData.EnergyMarketPeriod = _data.CommonData.PlanPeriod;

            if(_currentState != null)
            {
                _battery.InitialSoC = _currentState.SoC;
            }

            _data.OptimizationId = Guid.NewGuid();
            
            _solution = await CallMultiAssetService(_data); 
          
            //_solution = CallMultiAssetService(_data).Result; 

            // update plan
            _plan = _solution.Solution.BatterySolution.NetCharge[_battery];

            _plannedSoC.Series = _solution.Solution.BatterySolution.SOC[_battery];

        }

     

        public void OnError(Exception error)
        {
            throw new NotImplementedException();
        }

        public void OnNext(BatteryState value)
        {
            _currentState = value;
        }

        public virtual void Subscribe(IObservable<BatteryState> provider)
        {
            if (provider != null)
                unsubscriber = provider.Subscribe(this);
        }

        public void OnCompleted()
        {
            this.Unsubscribe();
            
        }

        protected virtual void Unsubscribe()
        {
            unsubscriber.Dispose();
        }
    }
}
