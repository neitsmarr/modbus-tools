using ModbusTools.Core.Serial;

namespace ModbusTools.Core.Sniffing;

/// <summary>Immutable configuration of one capture.</summary>
public sealed record SniffOptions
{
    public const int DefaultCapacity = 10_000;
    public const int MaxCapacity = 100_000;

    /// <summary>
    /// Default wait for a reply before a request counts as unanswered. The master's own timeout is not visible on the
    /// bus, so this errs on the long side: a slow reply is still paired, and the next request settles things earlier.
    /// </summary>
    public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(1);

    public required SerialSettings Serial { get; init; }

    /// <summary>How long after the end of a request a reply is still paired with it.</summary>
    public TimeSpan ResponseTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = DefaultResponseTimeout;

    /// <summary>Log entries kept; older ones are dropped, while the statistics keep counting them.</summary>
    public int Capacity
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxCapacity);
            field = value;
        }
    } = DefaultCapacity;

    /// <summary>
    /// Silence after which bytes held back cannot be continued, so an incomplete frame counts as garbled. Frames are
    /// split by content, so this only ends broken ones; see <see cref="RtuTiming.DefaultFrameGap"/>.
    /// </summary>
    public TimeSpan FrameGap => RtuTiming.DefaultFrameGap(Serial);
}
