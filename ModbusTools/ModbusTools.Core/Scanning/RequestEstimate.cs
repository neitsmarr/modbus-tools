namespace ModbusTools.Core.Scanning;

/// <summary>
/// Range of request counts a run can take, since adaptive strategies depend on what the device answers.
/// </summary>
public readonly record struct RequestEstimate(int Minimum, int Maximum)
{
    public static RequestEstimate Exactly(int count) => new(count, count);

    public bool IsExact => Minimum == Maximum;

    public static RequestEstimate operator +(RequestEstimate left, RequestEstimate right) =>
        new(left.Minimum + right.Minimum, left.Maximum + right.Maximum);
}
