namespace ModbusTools.Core.BaudSweeping;

public enum BaudEdgeSide
{
    Lower,
    Upper,
}

/// <summary>Marks a visit as part of an edge pass: which pass, and which edge it checks.</summary>
/// <param name="Pass">1 for the first edge pass.</param>
public readonly record struct BaudEdgePass(int Pass, BaudEdgeSide Side);

/// <summary>What one edge pass found at an edge, against the outward sweep.</summary>
/// <param name="Pass">1 for the first edge pass.</param>
/// <param name="ShiftPercent">
/// How far the pass's own votes put the edge from where the outward sweep's put it, in percent of the expected rate:
/// positive outwards (the window got wider), negative inwards. When <paramref name="PastCheckedRates"/> is set it
/// moved at least this far.
/// </param>
/// <param name="PastCheckedRates">
/// The pass found no edge among the rates it checked, because they all worked or all failed: the edge had moved
/// beyond them.
/// </param>
/// <param name="Moved">
/// Whether some rate the pass checked answered so differently from the outward sweep that chance is an unlikely
/// explanation; see <see cref="BaudEdgeCheck.SignificanceLevel"/>. Otherwise the shift is noise.
/// </param>
public readonly record struct BaudEdgeShift(int Pass, double ShiftPercent, bool PastCheckedRates, bool Moved);

/// <summary>
/// What the edge passes found at one edge, compared with the outward sweep. Each pass's edge comes from its own votes
/// alone, so a clock that drifted in between shows up as a move instead of blending into the totals.
/// </summary>
/// <remarks>
/// The rate right at an edge answers some of the time, so from one visit to the next its share of replies - and the
/// edge interpolated from it - wobbles even when nothing has changed. A move is therefore only reported when a rate's
/// replies differ by more than chance explains: Fisher's exact test on its replies in the pass against those in the
/// outward sweep. A rate that went from always answering to never is far beyond chance; one that went from six
/// replies in ten to four is not.
/// </remarks>
/// <param name="Outward">The edge as the outward sweep found it, before any edge pass.</param>
/// <param name="Passes">What each edge pass that checked this edge found, in pass order.</param>
public sealed record BaudEdgeCheck(BaudEdgeSide Side, BaudWindowEdge Outward, IReadOnlyList<BaudEdgeShift> Passes)
{
    /// <summary>
    /// The chance of reporting a move for an edge that did not move: the usual 95 % confidence. It is shared among all
    /// the rates compared at that edge (the Bonferroni correction), since each comparison is another chance for a
    /// fluke.
    /// </summary>
    public const double SignificanceLevel = 0.05;

    /// <summary>Whether any pass moved the edge.</summary>
    public bool Moved => Passes.Any(shift => shift.Moved);

    /// <summary>The pass that moved the edge furthest, or null when none moved it.</summary>
    public BaudEdgeShift? LargestMove => Passes.Where(shift => shift.Moved)
        .Select(shift => (BaudEdgeShift?)shift)
        .MaxBy(shift => Math.Abs(shift!.Value.ShiftPercent));

    /// <summary>Compares every edge pass with the outward sweep, for each edge both found; empty without edge passes.</summary>
    public static IReadOnlyList<BaudEdgeCheck> Analyse(BaudSweepResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var grid = result.Grid;
        var outward = RateVotes.PerRate(result.Visits.Where(visit => visit.Step.EdgePass is null).Select(visit => visit.Votes));
        if (outward.Length == 0 || result.Visits.All(visit => visit.Step.EdgePass is null))
        {
            return [];
        }

        var best = BaudWindow.BestIndex(outward, grid.ExpectedBaudRate);
        if (!outward[best].IsWorking)
        {
            return [];
        }

        var outwardByRate = outward.ToDictionary(rate => rate.BaudRate);
        var checks = new List<BaudEdgeCheck>();
        foreach (var (side, direction) in new[] { (BaudEdgeSide.Lower, -1), (BaudEdgeSide.Upper, 1) })
        {
            if (BaudWindow.FindEdge(outward, best, direction, grid) is not BaudWindowEdge edge)
            {
                continue;
            }

            var passes = result.Visits
                .Where(visit => visit.Step.EdgePass?.Side == side)
                .GroupBy(visit => visit.Step.EdgePass!.Value.Pass)
                .OrderBy(pass => pass.Key)
                .Select(pass => (Pass: pass.Key, Rates: RateVotes.PerRate(pass.Select(visit => visit.Votes))))
                .Where(pass => pass.Rates.Length > 0)
                .ToList();
            if (passes.Count == 0)
            {
                continue;
            }

            var comparisons = passes.Sum(pass => pass.Rates.Count(rate => outwardByRate.ContainsKey(rate.BaudRate)));
            var threshold = SignificanceLevel / Math.Max(comparisons, 1);
            var shifts = passes
                .Select(pass => Shift(pass.Pass, pass.Rates, edge, direction, grid) with
                {
                    Moved = pass.Rates.Any(rate =>
                        outwardByRate.TryGetValue(rate.BaudRate, out var before) && FisherExact(before, rate) < threshold),
                })
                .ToList();
            checks.Add(new BaudEdgeCheck(side, edge, shifts));
        }

        return checks;
    }

    /// <summary>
    /// Finds the edge among one pass's rates the way the window analysis does, walking outwards from the rate nearest
    /// the middle of the window, and measures it against the outward sweep's.
    /// </summary>
    private static BaudEdgeShift Shift(int pass, RateVotes[] rates, BaudWindowEdge outward, int direction, BaudGrid grid)
    {
        var inner = direction > 0 ? 0 : rates.Length - 1;
        var outer = direction > 0 ? rates.Length - 1 : 0;
        var (offset, past) = rates[inner].IsWorking
            ? BaudWindow.FindEdge(rates, inner, direction, grid) is BaudWindowEdge edge
                ? (edge.OffsetPercent, false)
                : (grid.PercentOf(rates[outer].BaudRate), true)
            : (grid.PercentOf(rates[inner].BaudRate), true);

        // Subtracting in the edge's own direction keeps an unchanged edge at +0 rather than -0.
        var shift = direction > 0 ? offset - outward.OffsetPercent : outward.OffsetPercent - offset;
        return new BaudEdgeShift(pass, shift, past, Moved: false);
    }

    /// <summary>
    /// Two-sided p-value of Fisher's exact test: the chance of two sets of requests at one rate differing in their
    /// share of replies at least this much if the rate's chance of a reply had stayed the same.
    /// </summary>
    internal static double FisherExact(RateVotes first, RateVotes second)
    {
        var answered = first.Answered + second.Answered;
        var requests = first.Requests + second.Requests;
        var denominator = LogChoose(requests, answered);
        double Probability(int firstAnswered) => Math.Exp(
            LogChoose(first.Requests, firstAnswered) + LogChoose(second.Requests, answered - firstAnswered) - denominator);

        // Every split of the replies at least as unlikely as the one seen counts; the tolerance absorbs rounding.
        var observed = Probability(first.Answered);
        var p = 0.0;
        for (var split = Math.Max(0, answered - second.Requests); split <= Math.Min(first.Requests, answered); split++)
        {
            var probability = Probability(split);
            if (probability <= observed * (1 + 1e-7))
            {
                p += probability;
            }
        }

        return Math.Min(p, 1);
    }

    private static double LogChoose(int n, int k) => LogFactorial(n) - LogFactorial(k) - LogFactorial(n - k);

    private static double LogFactorial(int n)
    {
        var sum = 0.0;
        for (var i = 2; i <= n; i++)
        {
            sum += Math.Log(i);
        }

        return sum;
    }
}
