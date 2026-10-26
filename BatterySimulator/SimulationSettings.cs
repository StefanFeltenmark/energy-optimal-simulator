using BatterySimulator.Interfaces;

namespace BatterySimulator
{
    public enum MarketModel
    {
        /// <summary>Every trade fills completely at the hourly price (sell) or price + spread (buy).</summary>
        SinglePrice,

        /// <summary>15-minute products traded with market orders against a synthetic continuous-intraday order book.</summary>
        OrderBook
    }

    /// <summary>
    /// User-tunable inputs for a simulation run. The defaults reproduce the original hard-coded setup.
    /// </summary>
    public class SimulationSettings
    {
        /// <summary>Nord Pool hourly Elspot prices for 2018, all areas. The defaults below (and the unit tests) use it.</summary>
        public const string DefaultPriceFile = @"C:\Users\stefan.feltenmark\Documents\energy-optimal-simulator\Data\Prices\elspot-prices_2018_hourly_eur.csv";

        /// <summary>SE3 day-ahead prices from Volue Insight, 2025-10-01 to 2026-10-24, 15-minute products (scripts/fetch_volue_prices.py).</summary>
        public const string RecentPriceFile = @"C:\Users\stefan.feltenmark\Documents\energy-optimal-simulator\Data\Prices\se3_2025-10-01_2026-10-25_min15.csv";

        /// <summary>
        /// Settings on <see cref="RecentPriceFile"/>, the data the GUI, console and experiment runner start from.
        /// The start is a Thursday 09:00 like the 2018 default, after the October 2025 DST switch.
        /// </summary>
        public static SimulationSettings Recent() => new()
        {
            PriceFile = RecentPriceFile,
            ForecastFile = RecentForecastFile,
            PriceArea = "SE3",
            Start = new DateTime(2025, 11, 6, 9, 0, 0, DateTimeKind.Local)
        };

        /// <summary>
        /// SE3 day-ahead price forecasts for the same period as <see cref="RecentPriceFile"/>: for each delivery day,
        /// the latest Volue Insight forecast issued before day-ahead gate closure (scripts/fetch_volue_forecasts.py).
        /// </summary>
        public const string RecentForecastFile = @"C:\Users\stefan.feltenmark\Documents\energy-optimal-simulator\Data\Prices\se3_2025-10-01_2026-10-25_min15_da_forecast.csv";

        // Simulation
        public DateTime Start { get; set; } = new DateTime(2018, 1, 4, 9, 0, 0, DateTimeKind.Local);
        public int DurationHours { get; set; } = 24;
        public int TimeStepSeconds { get; set; } = 300;
        public int SleepMilliseconds { get; set; } = 250;
        public double PlanningHorizonHours { get; set; } = 8;
        public double ReplanIntervalMinutes { get; set; } = 180;

        // Market
        public string PriceFile { get; set; } = DefaultPriceFile;
        public string PriceArea { get; set; } = "SE3";
        public double BuySellSpread { get; set; } = 0.01;
        public MarketModel MarketModel { get; set; } = MarketModel.SinglePrice;

        // Synthetic order book (MarketModel.OrderBook). Products are 15-minute delivery periods.
        public const int ProductMinutes = global::BatterySimulator.Interfaces.ProductQuote.ProductMinutes;
        /// <summary>Trading in a product stops this long before its delivery starts.</summary>
        public double GateClosureMinutes { get; set; } = 30;
        /// <summary>
        /// Trading in a product opens at this local hour on the day before its delivery day; its mid then jumps
        /// from the day-ahead (price file) price by the opening deviation.
        /// </summary>
        public double TradingOpensHour { get; set; } = 15;

        // Mid-price model. Defaults fitted to SE3 day-ahead, IDA1-3 and hourly intraday index prices, 2025-10 to
        // 2026-10, by scripts/calibrate_order_book.py (see there for the method and its limits).
        /// <summary>
        /// Volatility far from delivery of the market-wide factor that moves all open products' mids together,
        /// EUR/MWh per sqrt(hour). Where a mid is at delivery is the product's realized intraday price. The fit
        /// gives 0: within a delivery day, the strongly correlated product noise carries the common moves.
        /// </summary>
        public double MidVolatility { get; set; } = 0;
        /// <summary>Volatility far from delivery of each product's own mid noise, EUR/MWh per sqrt(hour).</summary>
        public double ProductNoiseVolatility { get; set; } = 2.4;
        /// <summary>Correlation between the mid moves of consecutive quarter-hours of a delivery day (k apart: this to the k-th power).</summary>
        public double ProductNoiseCorrelation { get; set; } = 0.986;
        /// <summary>Volatility at delivery as a multiple of the volatility far from delivery (both volatilities).</summary>
        public double VolatilityAtDeliveryFactor { get; set; } = 4.4;
        /// <summary>Time constant of the volatility's rise towards delivery, hours.</summary>
        public double VolatilityBuildUpHours { get; set; } = 5.1;
        /// <summary>Std of the jump from the day-ahead price when trading opens, common to a delivery day's products, EUR/MWh.</summary>
        public double OpeningCommonStd { get; set; } = 4.1;
        /// <summary>Std of the per-product part of the jump from the day-ahead price when trading opens, EUR/MWh.</summary>
        public double OpeningProductStd { get; set; } = 6.6;
        /// <summary>Correlation of the per-product opening jumps of consecutive quarter-hours, apart from <see cref="OpeningQuarterShare"/>.</summary>
        public double OpeningProductCorrelation { get; set; } = 0.89;
        /// <summary>Share of the per-product opening jump's variance that is independent per quarter-hour.</summary>
        public double OpeningQuarterShare { get; set; } = 0.18;
        /// <summary>Bid/ask spread close to gate closure, EUR/MWh.</summary>
        public double SpreadNearDelivery { get; set; } = 0.5;
        /// <summary>Bid/ask spread long before delivery, EUR/MWh.</summary>
        public double SpreadFarFromDelivery { get; set; } = 3;
        /// <summary>Time constant for spread narrowing and depth building up towards delivery, hours.</summary>
        public double LiquidityBuildUpHours { get; set; } = 6;
        /// <summary>Number of price levels on each side of the book.</summary>
        public int BookLevels { get; set; } = 5;
        /// <summary>Price distance between consecutive levels, EUR/MWh.</summary>
        public double LevelPriceStep { get; set; } = 0.5;
        /// <summary>Volume per level close to delivery, MWh.</summary>
        public double LevelVolumeMWh { get; set; } = 1.0;
        /// <summary>Fraction of <see cref="LevelVolumeMWh"/> available long before delivery.</summary>
        public double FarDepthFraction { get; set; } = 0.3;
        /// <summary>Time constant for consumed liquidity to be replenished, minutes.</summary>
        public double LiquidityRecoveryMinutes { get; set; } = 30;
        /// <summary>Imbalance price = realized price + penalty when short, - penalty when long, EUR/MWh.</summary>
        public double ImbalancePenalty { get; set; } = 5;
        /// <summary>Seed for the synthetic mid-price paths (each product's path is derived from this and its delivery start).</summary>
        public int OrderBookSeed { get; set; } = 1;
        /// <summary>
        /// With the order book, give planners that support it (POMA, PriceLevel) the current bid/ask, depth and existing positions
        /// instead of the reference price, so revising a position costs the spread inside the optimisation.
        /// </summary>
        public bool BookAwarePlanning { get; set; } = true;
        /// <summary>
        /// Minimum change of a product's position worth trading before its last chance, MWh. Smaller revisions are
        /// left until the product is the front product (the last one tradable before its gate closure), which
        /// always trades fully. 0 trades every revision of the plan at once.
        /// </summary>
        public double IntradayTradeThresholdMWh { get; set; } = 0;

        // Day-ahead market (with the order book): bid the next day from a forecast, then trade intraday from there.
        /// <summary>
        /// Every day at <see cref="DayAheadGateClosureHour"/>, planners that support it bid the next day's volumes,
        /// planned against the day-ahead forecast in <see cref="ForecastFile"/>. Bids are price-independent (buys at
        /// the maximum price, sells from 0 EUR/MWh); the accepted volumes become the products' starting positions
        /// and are settled at the day-ahead price when it is published.
        /// </summary>
        public bool DayAheadMarket { get; set; } = false;
        /// <summary>Day-ahead price forecasts as known at gate closure, in the fetch script's CSV format.</summary>
        public string ForecastFile { get; set; } = "";
        /// <summary>Control experiment: bid against the actual day-ahead prices instead of the forecast.</summary>
        public bool DayAheadPerfectForecast { get; set; } = false;
        /// <summary>Local hour on the day before delivery when day-ahead bids must be in.</summary>
        public double DayAheadGateClosureHour { get; set; } = 12;
        /// <summary>Local hour on the day before delivery when day-ahead prices and accepted volumes are published.</summary>
        public double DayAheadPublicationHour { get; set; } = 13;

        // Battery
        public double PowerMW { get; set; } = 10;
        public double EnergyMWh { get; set; } = 20;
        public double ChargeEfficiency { get; set; } = 0.95;
        public double DischargeEfficiency { get; set; } = 0.95;
        public double InitialSoCPercent { get; set; } = 50;

        // Battery wear (BatteryWearModel.FromSettings builds POMA's degradation map from these). Placeholders, not
        // calibrated: the defaults cost about 5 EUR per MWh charged or discharged at 50 % SoC.
        /// <summary>Charge the cost of battery wear in the profit (and give it to the bound and planners).</summary>
        public bool BatteryWear { get; set; } = false;
        /// <summary>Give the wear model to planners that support it (POMA, PriceLevel); the profit includes wear either way.</summary>
        public bool WearAwarePlanning { get; set; } = true;
        /// <summary>Full cycles (a full charge plus a full discharge) at 50 % SoC until end of life.</summary>
        public double WearCycleLife { get; set; } = 6000;
        /// <summary>Remaining capacity at end of life, % of nominal.</summary>
        public double WearEndOfLifeCapacityPercent { get; set; } = 80;
        /// <summary>
        /// Cost of replacing what wears out (cells/modules, not inverters or grid connection) per MWh of nominal
        /// capacity, EUR/MWh. A rough guess; 120 000 (a whole system) gave about 10 EUR/MWh of wear and cut POMA's
        /// profit by 43 %.
        /// </summary>
        public double WearReplacementCostPerMWh { get; set; } = 60000;
        /// <summary>
        /// How many times faster cycling wears the battery at empty or full than at 50 % SoC. Anything but 1 multiplies
        /// power by an SoC factor and makes the map non-convex: POMA then reads it as its cheapest mix of points, under-
        /// estimates wear between them and plans part-power, tapering operation. 1 (the default) keeps the map convex,
        /// so POMA, the simulator and the bound read it alike; other values test POMA with a non-convex map.
        /// </summary>
        public double WearExtremeSoCFactor { get; set; } = 1;
        /// <summary>Extra calendar ageing while held full, % of nominal capacity per year, falling linearly to 0 at 50 % SoC.</summary>
        public double WearCalendarLossAtFullPercentPerYear { get; set; } = 1;

        // PriceLevelPlanner
        public double BuyThresholdPercent { get; set; } = 15;
        public double SellThresholdPercent { get; set; } = 15;
        public double LookAheadHours { get; set; } = 4;

        // POMA
        public double SocSoftMinPercent { get; set; } = 5;
        public double SocSoftMaxPercent { get; set; } = 95;

        /// <summary>
        /// POMA's degradation map built from the wear settings, or null with <see cref="BatteryWear"/> off. Points at
        /// SoC 0, 50 and 100 % and net charge -P, 0 and +P: cycling loses (100 - end-of-life capacity) % of nominal
        /// capacity over the cycle life (a cycle is a full charge plus a full discharge, counted at the grid
        /// connection), <see cref="WearExtremeSoCFactor"/> times faster at empty or full than at 50 %; holding the
        /// battery above 50 % adds calendar ageing rising to <see cref="WearCalendarLossAtFullPercentPerYear"/> at
        /// full. A lost MWh of capacity costs the replacement cost divided by the share of capacity lost over the
        /// battery's life.
        /// </summary>
        public BatteryWearModel? CreateWearModel()
        {
            if (!BatteryWear) return null;
            double lifeLoss = 1 - WearEndOfLifeCapacityPercent / 100.0;
            // Capacity lost per MWh through the grid connection: lifeLoss * E over 2 * E * cycle life MWh.
            double lossPerMWh = lifeLoss / (2 * WearCycleLife);
            double calendarAtFull = WearCalendarLossAtFullPercentPerYear / 100.0 * EnergyMWh / 8760;

            var points = new List<WearPoint>();
            foreach (double soc in new[] { 0.0, 0.5, 1.0 })
            {
                double cycleFactor = soc == 0.5 ? 1 : WearExtremeSoCFactor;
                double calendar = soc > 0.5 ? (soc - 0.5) / 0.5 * calendarAtFull : 0;
                foreach (double netCharge in new[] { -PowerMW, 0, PowerMW })
                    points.Add(new WearPoint(soc, netCharge, calendar + cycleFactor * lossPerMWh * Math.Abs(netCharge)));
            }
            return new BatteryWearModel(points, WearReplacementCostPerMWh / lifeLoss);
        }

        /// <summary>A copy, for components that must not see later edits (all properties are value types or strings).</summary>
        public SimulationSettings Clone() => (SimulationSettings)MemberwiseClone();

        /// <summary>
        /// Returns a description of every invalid value; empty when the settings are usable.
        /// Checks that need the price data (area, date range) are done in <see cref="BatterySimulator.SetUp(SimulationSettings)"/>.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (DurationHours <= 0) errors.Add("Duration must be positive.");
            if (TimeStepSeconds <= 0) errors.Add("Time step must be positive.");
            if (SleepMilliseconds < 0) errors.Add("Delay cannot be negative.");
            if (PlanningHorizonHours < 0.25) errors.Add("Planning horizon must be at least 15 minutes.");
            if (ReplanIntervalMinutes <= 0) errors.Add("Re-plan interval must be positive.");
            if (ReplanIntervalMinutes > PlanningHorizonHours * 60) errors.Add("Re-plan interval cannot exceed the planning horizon.");
            if (BuySellSpread < 0) errors.Add("Buy/sell spread cannot be negative.");
            if (PowerMW <= 0) errors.Add("Battery power must be positive.");
            if (EnergyMWh <= 0) errors.Add("Battery energy must be positive.");
            if (ChargeEfficiency is <= 0 or > 1) errors.Add("Charge efficiency must be in (0, 1].");
            if (DischargeEfficiency is <= 0 or > 1) errors.Add("Discharge efficiency must be in (0, 1].");
            if (InitialSoCPercent is < 0 or > 100) errors.Add("Initial SoC must be between 0 and 100 %.");
            if (BuyThresholdPercent is < 0 or > 100 || SellThresholdPercent is < 0 or > 100) errors.Add("Price thresholds must be between 0 and 100 %.");
            if (LookAheadHours <= 0) errors.Add("Look-ahead must be positive.");
            if (BatteryWear)
            {
                if (WearCycleLife <= 0) errors.Add("Cycle life must be positive.");
                if (WearEndOfLifeCapacityPercent is <= 0 or >= 100) errors.Add("End-of-life capacity must be between 0 and 100 %.");
                if (WearReplacementCostPerMWh < 0) errors.Add("Replacement cost cannot be negative.");
                if (WearExtremeSoCFactor < 0) errors.Add("Wear factor at empty or full cannot be negative.");
                if (WearCalendarLossAtFullPercentPerYear < 0) errors.Add("Calendar ageing cannot be negative.");
            }
            if (SocSoftMinPercent < 0 || SocSoftMaxPercent > 100 || SocSoftMinPercent >= SocSoftMaxPercent) errors.Add("Soft SoC limits must satisfy 0 <= min < max <= 100 %.");
            if (MarketModel == MarketModel.OrderBook)
            {
                if (TimeStepSeconds > 0 && (ProductMinutes * 60) % TimeStepSeconds != 0) errors.Add($"With the order book, the time step must divide {ProductMinutes} minutes ({ProductMinutes * 60} s).");
                // Prices are interpolated between grid points; trading inside a grid cell would see where they are heading.
                if (TimeStepSeconds % (SyntheticOrderBook.PriceGridMinutes * 60) != 0) errors.Add($"With the order book, the time step must be a multiple of {SyntheticOrderBook.PriceGridMinutes} minutes.");
                if (Start.Minute % ProductMinutes != 0 || Start.Second != 0) errors.Add($"With the order book, the start must be on a {ProductMinutes}-minute boundary.");
                // Plans must start on product boundaries and cover whole products.
                if (ReplanIntervalMinutes % ProductMinutes != 0) errors.Add($"With the order book, the re-plan interval must be a multiple of {ProductMinutes} minutes.");
                if (PlanningHorizonHours * 60 % ProductMinutes != 0) errors.Add($"With the order book, the planning horizon must be a multiple of {ProductMinutes} minutes.");
                if (GateClosureMinutes < 0) errors.Add("Gate closure cannot be negative.");
                if (TradingOpensHour is < 0 or >= 24) errors.Add("Trading opening hour must be in [0, 24).");
                if (MidVolatility < 0 || ProductNoiseVolatility < 0) errors.Add("Mid-price volatilities cannot be negative.");
                if (ProductNoiseCorrelation is < 0 or >= 1 || OpeningProductCorrelation is < 0 or >= 1) errors.Add("Correlations between quarter-hours must be in [0, 1).");
                if (VolatilityAtDeliveryFactor <= 0) errors.Add("Volatility factor at delivery must be positive.");
                if (VolatilityBuildUpHours <= 0) errors.Add("Volatility build-up time must be positive.");
                if (OpeningCommonStd < 0 || OpeningProductStd < 0) errors.Add("Opening deviations cannot be negative.");
                if (OpeningQuarterShare is < 0 or > 1) errors.Add("Opening quarter-hour share must be between 0 and 1.");
                if (SpreadNearDelivery < 0 || SpreadFarFromDelivery < 0) errors.Add("Spreads cannot be negative.");
                if (LiquidityBuildUpHours <= 0) errors.Add("Liquidity build-up time must be positive.");
                if (BookLevels < 1) errors.Add("The book needs at least one level per side.");
                if (LevelPriceStep < 0) errors.Add("Level price step cannot be negative.");
                if (LevelVolumeMWh <= 0) errors.Add("Level volume must be positive.");
                if (FarDepthFraction is < 0 or > 1) errors.Add("Far-from-delivery depth fraction must be between 0 and 1.");
                if (LiquidityRecoveryMinutes <= 0) errors.Add("Liquidity recovery time must be positive.");
                if (ImbalancePenalty < 0) errors.Add("Imbalance penalty cannot be negative.");
                if (IntradayTradeThresholdMWh < 0) errors.Add("Intraday trade threshold cannot be negative.");
            }
            if (DayAheadMarket)
            {
                if (MarketModel != MarketModel.OrderBook) errors.Add("The day-ahead market needs the order book market model.");
                if (string.IsNullOrWhiteSpace(ForecastFile)) errors.Add("The day-ahead market needs a forecast file.");
                if (DayAheadGateClosureHour is < 0 or >= 24 || DayAheadPublicationHour <= DayAheadGateClosureHour || DayAheadPublicationHour > TradingOpensHour)
                    errors.Add("Day-ahead times must satisfy 0 <= gate closure < publication <= intraday opening < 24 h.");
                // Gate closure and publication must fall on a simulation step.
                if (TimeStepSeconds > 0 && ((DayAheadGateClosureHour * 3600) % TimeStepSeconds != 0 || (DayAheadPublicationHour * 3600) % TimeStepSeconds != 0))
                    errors.Add("Day-ahead gate closure and publication must be on a time step boundary.");
            }
            return errors;
        }
    }
}
