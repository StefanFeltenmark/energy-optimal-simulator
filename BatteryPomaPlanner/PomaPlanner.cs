using BatterySimulator;
using BatterySimulator.Interfaces;
using Domain;
using Powel.Optimal.Domain.Infrastructure.Interfaces;
using Powel.Optimal.MultiAsset.Domain;
using Powel.Optimal.MultiAsset.Domain.Common;
using Powel.Optimal.MultiAsset.Infrastructure;
using Powel.Optimal.MultiAsset.Domain.Common.Market;
using Powel.Optimal.MultiAsset.Domain.Common.Solution;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage;
using Powel.Optimal.MultiAsset.Domain.EnergyStorage.Providers;
using Powel.Optimal.MultiAsset.Domain.General.Data;
using Powel.Optimal.MultiAsset.Domain.Quantities;


namespace BatteryPomaPlanner
{
    public class PomaPlanner : IBatteryPlanner, IOrderBookAwarePlanner, IObserver<BatteryState>
    {
        private const double Unlimited = 1000000;
        private TimeSeries _referenceBuyPrice = new TimeSeries();
        private TimeSeries _referenceSellPrice = new TimeSeries();
        private IReadOnlyList<ProductQuote>? _marketView;
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

        /// <summary>Soft lower SoC limit as a fraction (0-1); applied on every optimisation.</summary>
        public double SocSoftMin { get; set; } = 0.05;

        /// <summary>Soft upper SoC limit as a fraction (0-1); applied on every optimisation.</summary>
        public double SocSoftMax { get; set; } = 0.95;

        public void SetUp(Battery battery, EnergyMarket market)
        {
            PriceUnit euroPerMw = new PriceUnit(Currencies.Euro, Units.MegaWatt);

            _battery = battery;
            _battery.CalculateInitialCapacityC(); // should be done in POMA
            _battery.CalculateInitialCapacityE(); // should be done in POMA
            _data.EnergyStorage = new EnergyStorageData
            {
                Batteries =
                [
                    battery
                ]
            };
            _data.CommonData = new CommonData();

            // POMA gets its own market: the simulator's prices are keyed by local clock time,
            // while POMA works in UTC (PlanningPeriod converts its start to UTC).
            _market = new EnergyMarket(market.Id, market.Name);
            _referenceSellPrice = ToUtc(market.Ts.EnergySellPrice);
            _referenceBuyPrice = ToUtc(market.Ts.EnergyBuyPrice);
            _market.Ts.EnergySellPrice = _referenceSellPrice;
            _market.Ts.EnergyBuyPrice = _referenceBuyPrice;

            _data.CommonData.EnergyMarkets.Add(_market);

            // Market data
            _market.Ts.PowerBuyMax = Constant(Unlimited);
            _market.Ts.PowerBuyMin = new TimeSeries();
            _market.Ts.PowerSellMin = new TimeSeries();
            _market.Ts.PowerSellMax = Constant(Unlimited);
            _market.Ts.EnergyDeficitPenaltyPrice = new TimeSeries();
            _market.Ts.EnergyDeficitPenaltyPrice.DefaultValue = 1000000;
            _market.Ts.EnergySurplusPenaltyPrice = new TimeSeries();
            _market.Ts.EnergySurplusPenaltyPrice.DefaultValue = 1000000;

            _market.Ts.PowerLoad = new TimeSeries();

            _market.EnergyProviders = new List<EnergyProvider>();
            BatteryEnergyProvider provider = new BatteryEnergyProvider(_market.Id, _battery.Id)
            {
                Battery = _battery,
                EnergyMarket = _market,
                Availability = new TimeSeries()
            };
            provider.Availability.DefaultValue = 1;
            provider.MaxDelivery = new TimeSeries();
            // Delivery limits must not cap the battery's own power rating.
            double deliveryLimit = Math.Max(100, _battery.NominalChargeCapacity.CovertToUnit(Units.MegaWatt).Value);
            provider.MaxDelivery.DefaultValue = deliveryLimit;
            provider.MinDelivery = new TimeSeries();
            provider.MinDelivery.DefaultValue = -deliveryLimit;
            
            _market.EnergyProviders.Add(provider);

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
            _battery.Ts.SocSoftMax.DefaultValue = SocSoftMax;
            _battery.Ts.SocSoftMin = new TimeSeries();
            _battery.Ts.SocSoftMin.DefaultValue = SocSoftMin;
            _battery.FinalSocMax = new Percentage(100);
            _battery.FinalSocMin = new Percentage(0);
            _battery.FinalSocPenaltyPrice = new UnitPrice(1000, new PriceUnit(Currencies.Euro, Units.MegaWattHour));
            _battery.SoftSchedulePenaltyPrice = new UnitPrice(1000, new PriceUnit(Currencies.Euro, Units.MegaWattHour));
            // FinalStoragePrice is set per optimisation in UpdatePlan.

            _data.CommonData.Parameters.CaseName = "BatterySimulation";
            _data.OptimizationId = _optimizationId;

            _data.CommonData.SolveParameters.LogLevel = 2;
        }

        public string Name => "POMA";

        public void SetMarketView(IReadOnlyList<ProductQuote>? quotes) => _marketView = quotes;

        /// <summary>
        /// Sets the market series for the next optimisation. With a market view, buy/sell prices and limits come
        /// from the order book and existing positions enter as a negative load (a bought position is energy the
        /// portfolio must take up), so POMA's buys and sells are only the additional trades. POMA's energy balance:
        /// production + buy = load + sell, hence production = sell - buy - position.
        /// Without a view, it plans against the reference price as before.
        /// </summary>
        private void ApplyMarketView()
        {
            if (_marketView == null || _marketView.Count == 0)
            {
                _market.Ts.EnergyBuyPrice = _referenceBuyPrice;
                _market.Ts.EnergySellPrice = _referenceSellPrice;
                _market.Ts.PowerBuyMax = Constant(Unlimited);
                _market.Ts.PowerSellMax = Constant(Unlimited);
                _market.Ts.PowerLoad = new TimeSeries();
                return;
            }

            TimeSeries buyPrice = Breakpoints(), sellPrice = Breakpoints(), buyMax = Breakpoints(), sellMax = Breakpoints(), load = Breakpoints();
            foreach (ProductQuote quote in _marketView)
            {
                DateTime utc = DateTime.SpecifyKind(quote.Product, DateTimeKind.Local).ToUniversalTime();
                buyPrice.SetValueAt(utc, quote.BuyPrice);
                sellPrice.SetValueAt(utc, quote.SellPrice);
                buyMax.SetValueAt(utc, quote.MaxBuyMW);
                sellMax.SetValueAt(utc, quote.MaxSellMW);
                load.SetValueAt(utc, -quote.PositionMW);
            }

            // Close the series at the end of the last product so nothing carries on beyond the view.
            ProductQuote last = _marketView[^1];
            DateTime end = DateTime.SpecifyKind(last.Product, DateTimeKind.Local).ToUniversalTime().AddMinutes(ProductQuote.ProductMinutes);
            buyPrice.SetValueAt(end, last.BuyPrice);
            sellPrice.SetValueAt(end, last.SellPrice);
            buyMax.SetValueAt(end, 0);
            sellMax.SetValueAt(end, 0);
            load.SetValueAt(end, 0);

            _market.Ts.EnergyBuyPrice = buyPrice;
            _market.Ts.EnergySellPrice = sellPrice;
            _market.Ts.PowerBuyMax = buyMax;
            _market.Ts.PowerSellMax = sellMax;
            _market.Ts.PowerLoad = load;
        }

        private static TimeSeries Breakpoints() => new TimeSeries(true, true) { IsBreakPointSeries = true };

        private static TimeSeries Constant(double value) => new TimeSeries { DefaultValue = value };

        private static TimeSeries ToUtc(TimeSeries localSeries)
        {
            var utcSeries = new TimeSeries(true, true);
            foreach (DateTime t in localSeries.TimePoints())
            {
                utcSeries.SetValueAt(DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime(), localSeries[t]);
            }
            utcSeries.IsBreakPointSeries = localSeries.IsBreakPointSeries;
            return utcSeries;
        }


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

            // May have changed since the last optimisation (e.g. edited in the GUI).
            _battery.Ts.SocSoftMin.DefaultValue = SocSoftMin;
            _battery.Ts.SocSoftMax.DefaultValue = SocSoftMax;

            // Value energy left at the end of the horizon at what it can be sold for later:
            // the mean sell price over the next 24 h, net of discharge losses.
            DateTime startUtc = planStart.ToUniversalTime();
            double meanSellPrice = _referenceSellPrice.GetSubSeries(startUtc, startUtc.AddHours(24)).Values().Average();

            ApplyMarketView();
            _battery.FinalStoragePrice = new UnitPrice(meanSellPrice * _battery.DischargeEfficiency.Value,
                new PriceUnit(Currencies.Euro, Units.MegaWattHour));

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
