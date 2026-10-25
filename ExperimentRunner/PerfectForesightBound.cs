using Google.OrTools.LinearSolver;

namespace ExperimentRunner
{
    /// <summary>The perfect-foresight schedule and the profit it earns.</summary>
    public record BoundResult(double Profit, double[] ChargeMW, double[] DischargeMW, double[] EnergyMWh);

    /// <summary>
    /// Best profit any strategy could make with perfect knowledge of the prices: an LP over the same battery
    /// model and cash booking as the simulator (grid-side charge/discharge per step, charge and discharge
    /// efficiencies, energy between empty and full, buy at the buy price and sell at the sell price, no value
    /// for energy left at the end). Allowing simultaneous charge and discharge only loosens the LP, so it is
    /// still an upper bound.
    /// </summary>
    public static class PerfectForesightBound
    {
        /// <param name="buyPrices">Price paid per MWh charged in each step, EUR/MWh.</param>
        /// <param name="sellPrices">Price received per MWh discharged in each step, EUR/MWh.</param>
        /// <param name="minFinalEnergyMWh">
        /// Optional lower bound on the energy left at the end. 0 gives the true upper bound; the initial energy
        /// gives a fairer reference for planners that do not empty the battery.
        /// </param>
        public static BoundResult Solve(IReadOnlyList<double> buyPrices, IReadOnlyList<double> sellPrices, double stepHours,
            double powerMW, double energyMWh, double initialEnergyMWh, double chargeEfficiency, double dischargeEfficiency,
            double minFinalEnergyMWh = 0)
        {
            if (buyPrices.Count != sellPrices.Count) throw new ArgumentException("Buy and sell prices must have the same length.");
            int n = buyPrices.Count;

            using Solver solver = Solver.CreateSolver("GLOP") ?? throw new InvalidOperationException("GLOP solver not available.");
            var charge = new Variable[n];
            var discharge = new Variable[n];
            var energy = new Variable[n];
            Objective objective = solver.Objective();
            objective.SetMaximization();

            for (int k = 0; k < n; k++)
            {
                charge[k] = solver.MakeNumVar(0, powerMW, $"c{k}");
                discharge[k] = solver.MakeNumVar(0, powerMW, $"d{k}");
                // Energy at the end of step k.
                energy[k] = solver.MakeNumVar(k == n - 1 ? minFinalEnergyMWh : 0, energyMWh, $"e{k}");

                objective.SetCoefficient(charge[k], -buyPrices[k] * stepHours);
                objective.SetCoefficient(discharge[k], sellPrices[k] * stepHours);

                // e[k] - e[k-1] - etaC * c * dt + d * dt / etaD = 0, with e[-1] = initial energy.
                Constraint balance = solver.MakeConstraint(k == 0 ? initialEnergyMWh : 0, k == 0 ? initialEnergyMWh : 0, $"balance{k}");
                balance.SetCoefficient(energy[k], 1);
                if (k > 0) balance.SetCoefficient(energy[k - 1], -1);
                balance.SetCoefficient(charge[k], -chargeEfficiency * stepHours);
                balance.SetCoefficient(discharge[k], stepHours / dischargeEfficiency);
            }

            Solver.ResultStatus status = solver.Solve();
            if (status != Solver.ResultStatus.OPTIMAL)
                throw new InvalidOperationException($"Perfect-foresight LP not solved to optimality: {status}.");

            return new BoundResult(
                objective.Value(),
                charge.Select(v => v.SolutionValue()).ToArray(),
                discharge.Select(v => v.SolutionValue()).ToArray(),
                energy.Select(v => v.SolutionValue()).ToArray());
        }

        /// <summary>
        /// The bound for a simulator after <c>SetUp</c>, over its own period and time step. With the order book,
        /// prices are the realized prices on both sides (the book's mids converge to them and imbalance is settled
        /// around them), which bounds the expected profit, not every seed.
        /// </summary>
        public static BoundResult ForSimulation(BatterySimulator.BatterySimulator simulator, double minFinalEnergyMWh = 0)
        {
            var settings = simulator.Settings;
            TimeSpan step = TimeSpan.FromSeconds(settings.TimeStepSeconds);
            bool singlePrice = settings.MarketModel == BatterySimulator.MarketModel.SinglePrice;

            var buy = new List<double>();
            var sell = new List<double>();
            for (DateTime t = simulator.Start; t < simulator.End; t += step)
            {
                // Same lookups as the simulator's PnL booking: prices valid at the start of the step.
                sell.Add(simulator.SellPrices[t]);
                buy.Add(singlePrice ? simulator.BuyPrices[t] : simulator.SellPrices[t]);
            }

            return Solve(buy, sell, step.TotalHours, settings.PowerMW, settings.EnergyMWh,
                settings.EnergyMWh * settings.InitialSoCPercent / 100.0,
                settings.ChargeEfficiency, settings.DischargeEfficiency, minFinalEnergyMWh);
        }
    }
}
