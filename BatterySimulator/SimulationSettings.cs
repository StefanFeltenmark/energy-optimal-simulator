namespace BatterySimulator
{
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
            return errors;
        }
    }
}
