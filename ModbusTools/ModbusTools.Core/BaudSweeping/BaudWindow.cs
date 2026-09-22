namespace ModbusTools.Core.BaudSweeping;

/// <summary>How much of the working window the results have pinned down.</summary>
public enum BaudWindowStatus
{
    /// <summary>Nothing has been tried yet.</summary>
    NotEnoughData,

    /// <summary>No rate answered at least half its requests: wrong ID, frame format, wiring, or expected rate.</summary>
    NoWorkingRate,

    /// <summary>Every rate tried worked: the span is too narrow, or the port ignores the baud rate.</summary>
    Unbounded,

    /// <summary>One edge was found, the other is still outside the sweep.</summary>
    PartlyBounded,

    /// <summary>Both edges were found.</summary>
    Bounded,
}

/// <param name="BaudRate">Interpolated rate at which half the requests are answered.</param>
/// <param name="OffsetPercent">Distance of that rate from the expected rate, in percent.</param>
/// <param name="LastWorkingRate">Outermost rate that still worked.</param>
/// <param name="FirstFailingRate">First rate past it that did not.</param>
public readonly record struct BaudWindowEdge(
    double BaudRate,
    double OffsetPercent,
    int LastWorkingRate,
    int FirstFailingRate);

/// <summary>
/// What the results say about the range of rates the device answers on: its two edges, the rate in the middle - the
/// best estimate of the device's own baud rate - and how much room is left before communication fails.
/// </summary>
/// <remarks>
/// An edge is where the share of answered requests crosses one half, interpolated between the rate that still worked
/// and the first one that did not, so a single unlucky failure does not split the window. The middle is the average
/// of the two edges: both the device and the host tolerate roughly the same mismatch either way, so the window sits
/// symmetrically around the rate the device really runs at.
/// </remarks>
public sealed record BaudWindow
{
    public required BaudGrid Grid { get; init; }

    public required BaudWindowStatus Status { get; init; }

    public BaudWindowEdge? Lower { get; init; }

    public BaudWindowEdge? Upper { get; init; }

    /// <summary>The rate with the most replies, i.e. the middle of the working range as measured; null if nothing was tried.</summary>
    public int? BestRate { get; init; }

    public required int TestedRates { get; init; }

    public required int WorkingRates { get; init; }

    public required int RefusedRates { get; init; }

    /// <summary>True when no request at all was answered, as opposed to answered too rarely.</summary>
    public required bool AnythingAnswered { get; init; }

    /// <summary>The device's own baud rate: the middle of the window. Null until both edges are known.</summary>
    public double? CenterBaudRate =>
        Lower is BaudWindowEdge low && Upper is BaudWindowEdge high ? (low.BaudRate + high.BaudRate) / 2 : null;

    public double? CenterOffsetPercent => CenterBaudRate is double center ? Grid.PercentOf(center) : null;

    /// <summary>Half the width of the window: the mismatch this device and this adapter survive, in percent.</summary>
    public double? TolerancePercent =>
        Lower is BaudWindowEdge low && Upper is BaudWindowEdge high && CenterBaudRate is double center
            ? (high.BaudRate - low.BaudRate) / 2 / center * 100
            : null;

    /// <summary>Room below the expected rate before replies stop, in percent. Negative if it is already outside.</summary>
    public double? MarginDownPercent => Lower is BaudWindowEdge low ? -low.OffsetPercent : null;

    /// <summary>Room above the expected rate before replies stop, in percent.</summary>
    public double? MarginUpPercent => Upper?.OffsetPercent;

    /// <summary>The smaller of the two margins: the room for clock drift in the worse direction.</summary>
    public double? DriftMarginPercent => MarginDownPercent is double down && MarginUpPercent is double up
        ? Math.Min(down, up)
        : null;

    /// <summary>
    /// The device's baud rate as a range, given how far the port's own clock may be off. The measurement is relative
    /// to that clock, so its error carries straight over; the sweep's own resolution is half a step on each edge.
    /// </summary>
    public (double Low, double High)? EstimateDeviceRate(double? adapterAccuracyPercent)
    {
        if (CenterBaudRate is not double center)
        {
            return null;
        }

        var uncertainty = center * (adapterAccuracyPercent.GetValueOrDefault() + Grid.StepPercent / 2) / 100;
        return (center - uncertainty, center + uncertainty);
    }

    public static BaudWindow Analyse(BaudSweepResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var points = result.Rates.Where(rate => rate.Requests > 0).ToArray();
        var common = new BaudWindow
        {
            Grid = result.Grid,
            Status = BaudWindowStatus.NotEnoughData,
            TestedRates = points.Length,
            WorkingRates = points.Count(point => point.IsWorking),
            RefusedRates = result.RefusedRates,
            AnythingAnswered = points.Any(point => point.Answered > 0),
        };

        if (points.Length == 0)
        {
            return common;
        }

        var best = BestIndex(points, result.Grid.ExpectedBaudRate);
        common = common with { BestRate = points[best].BaudRate };
        if (!points[best].IsWorking)
        {
            return common with { Status = BaudWindowStatus.NoWorkingRate };
        }

        var lower = FindEdge(points, best, -1, result.Grid);
        var upper = FindEdge(points, best, 1, result.Grid);
        return common with
        {
            Lower = lower,
            Upper = upper,
            Status = (lower, upper) switch
            {
                (not null, not null) => BaudWindowStatus.Bounded,
                (null, null) => BaudWindowStatus.Unbounded,
                _ => BaudWindowStatus.PartlyBounded,
            },
        };
    }

    /// <summary>The rate with the highest share of replies; ties go to the one closest to the expected rate.</summary>
    private static int BestIndex(BaudRateResult[] points, int expectedBaudRate)
    {
        var best = 0;
        for (var i = 1; i < points.Length; i++)
        {
            var difference = points[i].SuccessRate - points[best].SuccessRate;
            if (difference > 0 || (difference == 0 &&
                Math.Abs(points[i].BaudRate - expectedBaudRate) < Math.Abs(points[best].BaudRate - expectedBaudRate)))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Walks out from the best rate until a rate fails, and returns where the reply share crossed one half between
    /// the two. Null when the walk runs out of results, i.e. the edge is beyond what was swept.
    /// </summary>
    private static BaudWindowEdge? FindEdge(BaudRateResult[] points, int from, int direction, BaudGrid grid)
    {
        for (var i = from + direction; i >= 0 && i < points.Length; i += direction)
        {
            if (points[i].IsWorking)
            {
                continue;
            }

            var working = points[i - direction];
            var failing = points[i];
            var share = (0.5 - working.SuccessRate) / (failing.SuccessRate - working.SuccessRate);
            var crossing = working.BaudRate + share * (failing.BaudRate - working.BaudRate);
            return new BaudWindowEdge(crossing, grid.PercentOf(crossing), working.BaudRate, failing.BaudRate);
        }

        return null;
    }
}
