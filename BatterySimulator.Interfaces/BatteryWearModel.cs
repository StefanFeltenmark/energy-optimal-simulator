namespace BatterySimulator.Interfaces;

/// <summary>One point of the wear map: capacity lost per hour at a state of charge and grid-side net charge.</summary>
/// <param name="SoC">State of charge as a fraction of nominal capacity (0-1).</param>
/// <param name="NetChargeMW">Grid-side discharge minus charge, MW (POMA's sign: positive discharges).</param>
/// <param name="CapacityLossMWhPerHour">Energy capacity lost per hour at this point, MWh/h.</param>
public record WearPoint(double SoC, double NetChargeMW, double CapacityLossMWhPerHour);

/// <summary>
/// Battery wear given as POMA's advanced degradation map (<c>Battery.DegradationMapE</c>): capacity lost per hour at
/// points of state of charge and net charge over the whole operating range, and a cost per MWh of capacity lost
/// (POMA's <c>DegradationCost</c>). The map can be read between its points in two ways:
/// <list type="bullet">
/// <item><see cref="LossRate"/>, the battery's wear: the map as a lookup table, interpolated bilinearly between the
/// neighbouring grid points. The simulator charges this.</item>
/// <item><see cref="EnvelopeLossRate"/>, POMA's reading: its LP mixes any points with convex weights that reproduce
/// the SoC and net charge and picks the cheapest mix, i.e. the lower convex envelope of the points.</item>
/// </list>
/// The envelope is never above the interpolation (an interpolation is one of the mixes), and the two agree where the
/// map is convex, e.g. when wear is a power part plus an SoC part. Where they differ, POMA under-estimates wear.
/// </summary>
public class BatteryWearModel
{
    private readonly WearPoint[] _points;
    private readonly double _minSoC, _maxSoC, _minNetCharge, _maxNetCharge;
    // The lookup table, when the points form a full grid: losses[i, j] at socs[i] and netCharges[j].
    private readonly double[]? _socs, _netCharges;
    private readonly double[,]? _losses;

    public BatteryWearModel(IEnumerable<WearPoint> points, double costPerMWhLost)
    {
        _points = points.ToArray();
        if (_points.Length < 3) throw new ArgumentException("The wear map needs at least three points.");
        if (_points.Any(p => p.CapacityLossMWhPerHour < 0)) throw new ArgumentException("Wear map losses cannot be negative.");
        if (costPerMWhLost < 0) throw new ArgumentException("The cost of lost capacity cannot be negative.");
        _minSoC = _points.Min(p => p.SoC);
        _maxSoC = _points.Max(p => p.SoC);
        _minNetCharge = _points.Min(p => p.NetChargeMW);
        _maxNetCharge = _points.Max(p => p.NetChargeMW);
        // Any state inside the rectangle can occur (the battery is truncated at empty and full), and POMA is
        // infeasible outside the points' convex hull, so the hull must be the whole rectangle.
        foreach (double soc in new[] { _minSoC, _maxSoC })
        foreach (double netCharge in new[] { _minNetCharge, _maxNetCharge })
            if (!_points.Any(p => p.SoC == soc && p.NetChargeMW == netCharge))
                throw new ArgumentException($"The wear map must contain the corners of its range; ({soc}, {netCharge} MW) is missing.");
        if (_minSoC == _maxSoC || _minNetCharge == _maxNetCharge) throw new ArgumentException("The wear map must span a range of SoC and net charge.");
        CostPerMWhLost = costPerMWhLost;

        double[] socs = _points.Select(p => p.SoC).Distinct().Order().ToArray();
        double[] netCharges = _points.Select(p => p.NetChargeMW).Distinct().Order().ToArray();
        if (socs.Length * netCharges.Length == _points.Length)
        {
            var losses = new double[socs.Length, netCharges.Length];
            var filled = new bool[socs.Length, netCharges.Length];
            foreach (WearPoint p in _points)
            {
                int i = Array.IndexOf(socs, p.SoC), j = Array.IndexOf(netCharges, p.NetChargeMW);
                losses[i, j] = p.CapacityLossMWhPerHour;
                filled[i, j] = true;
            }
            if (filled.Cast<bool>().All(f => f)) (_socs, _netCharges, _losses) = (socs, netCharges, losses);
        }
    }

    public IReadOnlyList<WearPoint> Points => _points;

    /// <summary>Cost of one MWh of energy capacity lost, EUR/MWh.</summary>
    public double CostPerMWhLost { get; }

    /// <summary>True if the points form a full grid (every SoC with every net charge), so they can be interpolated.</summary>
    public bool IsGrid => _losses != null;

    /// <summary>
    /// The battery's capacity loss per hour at <paramref name="soc"/> (fraction) and grid-side
    /// <paramref name="netChargeMW"/> (discharge positive): the map read as a lookup table, interpolated bilinearly
    /// within its grid cell. Values outside the map's range are clamped to it. Needs a grid map (<see cref="IsGrid"/>).
    /// </summary>
    public double LossRate(double soc, double netChargeMW)
    {
        if (_losses == null) throw new InvalidOperationException("Interpolating the wear map needs its points on a full grid (every SoC with every net charge).");
        soc = Math.Clamp(soc, _minSoC, _maxSoC);
        netChargeMW = Math.Clamp(netChargeMW, _minNetCharge, _maxNetCharge);
        (int i, double s) = Cell(_socs!, soc);
        (int j, double n) = Cell(_netCharges!, netChargeMW);
        return (1 - s) * (1 - n) * _losses[i, j] + (1 - s) * n * _losses[i, j + 1]
             + s * (1 - n) * _losses[i + 1, j] + s * n * _losses[i + 1, j + 1];
    }

    /// <summary>The lower index of the grid cell holding <paramref name="x"/> and the position within it (0-1).</summary>
    private static (int Index, double Fraction) Cell(double[] grid, double x)
    {
        int i = 0;
        while (i < grid.Length - 2 && x > grid[i + 1]) i++;
        return (i, (x - grid[i]) / (grid[i + 1] - grid[i]));
    }

    /// <summary>
    /// Capacity loss per hour as POMA's LP reads the map: the least loss over all convex combinations of the points
    /// that reproduce <paramref name="soc"/> (fraction) and <paramref name="netChargeMW"/> (discharge positive).
    /// Never above <see cref="LossRate"/>. Values outside the map's range are clamped to it. Any map, grid or not.
    /// </summary>
    public double EnvelopeLossRate(double soc, double netChargeMW)
    {
        soc = Math.Clamp(soc, _minSoC, _maxSoC);
        netChargeMW = Math.Clamp(netChargeMW, _minNetCharge, _maxNetCharge);

        // The LP's optimum is at a basic solution: at most three points with non-zero weight, affinely independent
        // since the hull has an area. So the envelope is the least loss over all triangles containing the state.
        double best = double.PositiveInfinity;
        int n = _points.Length;
        double sx = _maxSoC - _minSoC, sy = _maxNetCharge - _minNetCharge;
        double x = soc / sx, y = netChargeMW / sy;
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        for (int k = j + 1; k < n; k++)
        {
            WearPoint a = _points[i], b = _points[j], c = _points[k];
            // Barycentric weights from soc = sum w s_p, netCharge = sum w nc_p, sum w = 1, in scaled coordinates.
            double ax = a.SoC / sx, ay = a.NetChargeMW / sy;
            double bx = b.SoC / sx, by = b.NetChargeMW / sy;
            double cx = c.SoC / sx, cy = c.NetChargeMW / sy;
            double det = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
            if (Math.Abs(det) < 1e-12) continue;
            double wb = ((x - ax) * (cy - ay) - (cx - ax) * (y - ay)) / det;
            double wc = ((bx - ax) * (y - ay) - (x - ax) * (by - ay)) / det;
            double wa = 1 - wb - wc;
            const double tolerance = -1e-9;
            if (wa < tolerance || wb < tolerance || wc < tolerance) continue;
            double loss = wa * a.CapacityLossMWhPerHour + wb * b.CapacityLossMWhPerHour + wc * c.CapacityLossMWhPerHour;
            if (loss < best) best = loss;
        }
        return best;
    }

    /// <summary>Cost of the battery's wear (<see cref="LossRate"/>) over a step of <paramref name="hours"/>, EUR.</summary>
    public double Cost(double soc, double netChargeMW, double hours) => CostPerMWhLost * LossRate(soc, netChargeMW) * hours;

    /// <summary>
    /// Average wear cost per MWh traded through the grid connection at full power <paramref name="powerMW"/> and
    /// SoC <paramref name="soc"/>, charging and discharging alike, over what idling there costs, EUR/MWh.
    /// </summary>
    public double MarginalCostPerMWh(double powerMW, double soc = 0.5)
    {
        double idle = LossRate(soc, 0);
        double charge = LossRate(soc, -powerMW) - idle, discharge = LossRate(soc, powerMW) - idle;
        return CostPerMWhLost * (charge + discharge) / 2 / powerMW;
    }
}
