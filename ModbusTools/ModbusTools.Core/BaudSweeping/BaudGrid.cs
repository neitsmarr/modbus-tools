namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// The rates a sweep can try: the expected rate in the middle, then <see cref="StepPercent"/> apart out to
/// <see cref="SpanPercent"/> on either side.
/// </summary>
/// <remarks>
/// Strategies plan in step indices - 0 is the expected rate, negative is slower - so that whichever strategy runs,
/// the results land on the same rates and can be compared and drawn the same way. Steps are relative because what a
/// link tolerates is relative: a 1 % mismatch means the same thing at 9600 and at 115200.
/// </remarks>
public readonly record struct BaudGrid
{
    public const double MinStepPercent = 0.01;
    public const double MaxSpanPercent = 50;

    public BaudGrid(int expectedBaudRate, double spanPercent, double stepPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expectedBaudRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(spanPercent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(spanPercent, MaxSpanPercent);
        ArgumentOutOfRangeException.ThrowIfLessThan(stepPercent, MinStepPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stepPercent, spanPercent);

        ExpectedBaudRate = expectedBaudRate;
        SpanPercent = spanPercent;
        StepPercent = stepPercent;
    }

    /// <summary>The rate the device is believed to use; the middle of the sweep.</summary>
    public int ExpectedBaudRate { get; }

    public double SpanPercent { get; }

    public double StepPercent { get; }

    /// <summary>Steps from the expected rate to the end of the span.</summary>
    public int MaxIndex => (int)Math.Floor(SpanPercent / StepPercent + 1e-9);

    /// <summary>Rates in the grid, both sides plus the expected rate itself.</summary>
    public int Count => 2 * MaxIndex + 1;

    public int LowestBaudRate => RateAt(-MaxIndex);

    public int HighestBaudRate => RateAt(MaxIndex);

    public double PercentAt(int index) => index * StepPercent;

    /// <summary>The rate at a step index, rounded to whole baud as the browser's serial API requires.</summary>
    public int RateAt(int index) => Math.Max(1, (int)Math.Round(ExpectedBaudRate * (1 + PercentAt(index) / 100)));

    /// <summary>How far a rate is from the expected rate, in percent.</summary>
    public double PercentOf(double baudRate) => (baudRate - ExpectedBaudRate) * 100 / ExpectedBaudRate;

    /// <summary>Steps needed to cover <paramref name="percent"/> of the expected rate, at least one.</summary>
    public int StepsIn(double percent) => Math.Max(1, (int)Math.Ceiling(percent / StepPercent - 1e-9));

    public bool Contains(int index) => Math.Abs(index) <= MaxIndex;

    /// <summary>Indices of the rates from <paramref name="fromPercent"/> to <paramref name="toPercent"/>, both included, ascending.</summary>
    public IEnumerable<int> IndicesBetween(double fromPercent, double toPercent)
    {
        var first = Math.Max(-MaxIndex, (int)Math.Ceiling(fromPercent / StepPercent - 1e-9));
        var last = Math.Min(MaxIndex, (int)Math.Floor(toPercent / StepPercent + 1e-9));
        for (var index = first; index <= last; index++)
        {
            yield return index;
        }
    }

    /// <summary>Formats as e.g. "9600 ±10 % in 0.1 % steps".</summary>
    public override string ToString() => $"{ExpectedBaudRate} ±{SpanPercent:0.##} % in {StepPercent:0.###} % steps";
}
