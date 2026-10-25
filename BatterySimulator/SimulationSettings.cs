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
        public const string DefaultPriceFile = @"C:\Users\stefan.feltenmark\Documents\energy-optimal-simulator\Data\Prices\elspot-prices_2018_hourly_eur.csv";

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
        /// Trading in a product opens at this local hour on the day before its delivery day; its mid starts at the
        /// day-ahead (price file) price then.
        /// </summary>
        public double TradingOpensHour { get; set; } = 15;
        /// <summary>
        /// Volatility of the market-wide factor that moves all open products' mids together, EUR/MWh per sqrt(hour).
        /// Where a mid is at delivery is the product's realized intraday price.
        /// </summary>
        public double MidVolatility { get; set; } = 3;
        /// <summary>Volatility of each product's own, independent mid noise, EUR/MWh per sqrt(hour).</summary>
        public double ProductNoiseVolatility { get; set; } = 0.5;
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
        /// With the order book, give planners that support it (POMA) the current bid/ask, depth and existing positions
        /// instead of the reference price, so revising a position costs the spread inside the optimisation.
        /// </summary>
        public bool BookAwarePlanning { get; set; } = true;

        // Battery
        public double PowerMW { get; set; } = 10;
        public double EnergyMWh { get; set; } = 20;
        public double ChargeEfficiency { get; set; } = 0.95;
        public double DischargeEfficiency { get; set; } = 0.95;
        public double InitialSoCPercent { get; set; } = 50;

        // PriceLevelPlanner
        public double BuyThresholdPercent { get; set; } = 15;
        public double SellThresholdPercent { get; set; } = 15;
        public double LookAheadHours { get; set; } = 4;

        // POMA
        public double SocSoftMinPercent { get; set; } = 5;
        public double SocSoftMaxPercent { get; set; } = 95;

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
                if (SpreadNearDelivery < 0 || SpreadFarFromDelivery < 0) errors.Add("Spreads cannot be negative.");
                if (LiquidityBuildUpHours <= 0) errors.Add("Liquidity build-up time must be positive.");
                if (BookLevels < 1) errors.Add("The book needs at least one level per side.");
                if (LevelPriceStep < 0) errors.Add("Level price step cannot be negative.");
                if (LevelVolumeMWh <= 0) errors.Add("Level volume must be positive.");
                if (FarDepthFraction is < 0 or > 1) errors.Add("Far-from-delivery depth fraction must be between 0 and 1.");
                if (LiquidityRecoveryMinutes <= 0) errors.Add("Liquidity recovery time must be positive.");
                if (ImbalancePenalty < 0) errors.Add("Imbalance penalty cannot be negative.");
            }
            return errors;
        }
    }
}
