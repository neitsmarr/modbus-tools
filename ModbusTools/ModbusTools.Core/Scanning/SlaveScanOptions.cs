using ModbusTools.Core.Protocol;
using ModbusTools.Core.Serial;

namespace ModbusTools.Core.Scanning;

/// <summary>Immutable configuration of one scan pass over an explicit list of slave IDs.</summary>
public sealed record SlaveScanOptions
{
    public const int MaxRetries = 3;

    public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan DefaultInterRequestDelay = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan DefaultMaxFlushDuration = TimeSpan.FromMilliseconds(500);

    public required SerialSettings Serial { get; init; }

    /// <summary>IDs to probe, in order. Address 0 (broadcast) is not allowed.</summary>
    public required IReadOnlyList<byte> SlaveIds
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count == 0)
            {
                throw new ArgumentException("At least one slave ID is required.", nameof(SlaveIds));
            }

            if (value.Contains((byte)0))
            {
                throw new ArgumentException("Address 0 is broadcast and cannot be probed.", nameof(SlaveIds));
            }

            field = value.ToArray();
        }
    }

    public required ProbeRequest Probe { get; init; }

    public ITimeoutStrategy TimeoutStrategy { get; init; } = new FixedTimeoutStrategy(DefaultResponseTimeout);

    /// <summary>Additional attempts after one that did not find a device (0 to <see cref="MaxRetries"/>).</summary>
    public int Retries
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRetries);
            field = value;
        }
    }

    /// <summary>Pause after each transaction before the next request is sent.</summary>
    public TimeSpan InterRequestDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = DefaultInterRequestDelay;

    /// <summary>
    /// Silence that ends a received frame. Null uses <see cref="RtuTiming.DefaultFrameGap"/>; see
    /// <see cref="RtuTiming.MinimumFrameGap"/> for why this is longer than t3.5.
    /// </summary>
    public TimeSpan? FrameGap
    {
        get;
        init
        {
            if (value is TimeSpan gap)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gap, TimeSpan.Zero);
            }

            field = value;
        }
    }

    /// <summary>Upper bound on waiting for the line to go silent after a transaction.</summary>
    public TimeSpan MaxFlushDuration
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = DefaultMaxFlushDuration;

    /// <summary>End the scan after the first ID that answers with OK or an exception.</summary>
    public bool StopAtFirstFound { get; init; }

    public TimeSpan EffectiveFrameGap => FrameGap ?? RtuTiming.DefaultFrameGap(Serial);
}
