using BatterySimulator.Interfaces;
using Google.OrTools.LinearSolver;

namespace ExperimentRunner
{
    /// <summary>The perfect-foresight schedule, the profit it earns and its wear cost as the LP reads the map (included in the profit).</summary>
    public record BoundResult(double Profit, double[] ChargeMW, double[] DischargeMW, double[] EnergyMWh, double WearCost = 0);

    /// <summary>
    /// Best profit any strategy could make with perfect knowledge of the prices: an LP over the same battery
    /// model and cash booking as the simulator (grid-side charge/discharge per step, charge and discharge
    /// efficiencies, energy between empty and full, buy at the buy price and sell at the sell price, no value
    /// for energy left at the end, wear). Allowing simultaneous charge and discharge only loosens the LP, and the
    /// wear is POMA's reading of the map, which is never above the wear the simulator charges (its interpolation),
    /// so it is still an upper bound; exact where the map is convex.
    /// </summary>
    public static class PerfectForesightBound
    {
        /// <param name="buyPrices">Price paid per MWh charged in each step, EUR/MWh.</param>
        /// <param name="sellPrices">Price received per MWh discharged in each step, EUR/MWh.</param>
        /// <param name="minFinalEnergyMWh">
        /// Optional lower bound on the energy left at the end. 0 gives the true upper bound; the initial energy
        /// gives a fairer reference for planners that do not empty the battery.
        /// </param>
        /// <param name="wear">
        /// Battery wear, charged per step from the SoC at its start and its net charge, formulated as in POMA: convex
        /// weights on the map's points reproduce both, and the LP picks the cheapest weights, which is what
        /// <see cref="BatteryWearModel.EnvelopeLossRate"/> evaluates. (Interpolation is not convex in general, so an
        /// LP cannot charge it exactly.)
        /// </param>
        public static BoundResult Solve(IReadOnlyList<double> buyPrices, IReadOnlyList<double> sellPrices, double stepHours,
            double powerMW, double energyMWh, double initialEnergyMWh, double chargeEfficiency, double dischargeEfficiency,
            double minFinalEnergyMWh = 0, BatteryWearModel? wear = null)
        {
            if (buyPrices.Count != sellPrices.Count) throw new ArgumentException("Buy and sell prices must have the same length.");
            int n = buyPrices.Count;

            using Solver solver = Solver.CreateSolver("GLOP") ?? throw new InvalidOperationException("GLOP solver not available.");
            var charge = new Variable[n];
            var discharge = new Variable[n];
            var energy = new Variable[n];
            var wearWeights = new List<(Variable Weight, double Cost)>();
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

                if (wear != null)
                    AddWear(solver, objective, wear, k, charge[k], discharge[k], k > 0 ? energy[k - 1] : null, initialEnergyMWh, energyMWh, stepHours, wearWeights);
            }

            Solver.ResultStatus status = solver.Solve();
            if (status != Solver.ResultStatus.OPTIMAL)
                throw new InvalidOperationException($"Perfect-foresight LP not solved to optimality: {status}.");

            return new BoundResult(
                objective.Value(),
                charge.Select(v => v.SolutionValue()).ToArray(),
                discharge.Select(v => v.SolutionValue()).ToArray(),
                energy.Select(v => v.SolutionValue()).ToArray(),
                wearWeights.Sum(w => w.Weight.SolutionValue() * w.Cost));
        }

        /// <summary>
        /// POMA's degradation constraints for step <paramref name="k"/>: weights w_p >= 0 with sum w_p = 1,
        /// sum w_p nc_p = d - c and sum w_p soc_p E = the energy at the step's start, costing
        /// CostPerMWhLost * sum w_p loss_p * dt.
        /// </summary>
        private static void AddWear(Solver solver, Objective objective, BatteryWearModel wear, int k, Variable charge, Variable discharge,
            Variable? energyAtStart, double initialEnergyMWh, double energyMWh, double stepHours, List<(Variable, double)> weights)
        {
            Constraint convexity = solver.MakeConstraint(1, 1, $"wearConvexity{k}");
            Constraint netCharge = solver.MakeConstraint(0, 0, $"wearNetCharge{k}");
            netCharge.SetCoefficient(discharge, -1);
            netCharge.SetCoefficient(charge, 1);
            double initial = energyAtStart == null ? initialEnergyMWh : 0;
            Constraint soc = solver.MakeConstraint(initial, initial, $"wearSoC{k}");
            if (energyAtStart != null) soc.SetCoefficient(energyAtStart, -1);

            for (int p = 0; p < wear.Points.Count; p++)
            {
                WearPoint point = wear.Points[p];
                Variable weight = solver.MakeNumVar(0, double.PositiveInfinity, $"w{k}_{p}");
                convexity.SetCoefficient(weight, 1);
                netCharge.SetCoefficient(weight, point.NetChargeMW);
                soc.SetCoefficient(weight, point.SoC * energyMWh);
                double cost = wear.CostPerMWhLost * point.CapacityLossMWhPerHour * stepHours;
                objective.SetCoefficient(weight, -cost);
                weights.Add((weight, cost));
            }
        }

        /// <summary>
        /// The bound for a simulator after <c>SetUp</c>, over its own period and time step and with its wear. With the
        /// order book, prices are each product's realized intraday price on both sides (the mid at delivery, around
        /// which imbalance is settled). The book's mids are martingales ending there, so this bounds the expected
        /// profit over seeds, not every seed.
        /// </summary>
        public static BoundResult ForSimulation(BatterySimulator.BatterySimulator simulator, double minFinalEnergyMWh = 0)
        {
            var settings = simulator.Settings;
            TimeSpan step = TimeSpan.FromSeconds(settings.TimeStepSeconds);
            var book = simulator.Executor?.Book;

            var buy = new List<double>();
            var sell = new List<double>();
            for (DateTime t = simulator.Start; t < simulator.End; t += step)
            {
                // Same lookups as the simulator's PnL booking: prices valid at the start of the step.
                if (book == null)
                {
                    sell.Add(simulator.SellPrices[t]);
                    buy.Add(simulator.BuyPrices[t]);
                }
                else
                {
                    double realized = book.RealizedPrice(BatterySimulator.OrderBookExecutor.ProductStart(t));
                    sell.Add(realized);
                    buy.Add(realized);
                }
            }

            return Solve(buy, sell, step.TotalHours, settings.PowerMW, settings.EnergyMWh,
                settings.EnergyMWh * settings.InitialSoCPercent / 100.0,
                settings.ChargeEfficiency, settings.DischargeEfficiency, minFinalEnergyMWh, simulator.Wear);
        }
    }
}
