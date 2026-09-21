using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;
using ModbusTools.Core.Serial;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>Immutable configuration of one baud rate sweep against a single slave.</summary>
public sealed record BaudSweepOptions
{
    public const double DefaultSpanPercent = 10;
    public const double DefaultStepPercent = 0.1;
    public const double DefaultDeadBandPercent = 1;
    public const int DefaultRequestsPerRate = 10;
    public const int MaxRequestsPerRate = 100;
    public const int DefaultVerifyEdgePasses = 1;
    public const int MaxVerifyEdgePasses = 100;
    public const double MaxAdapterAccuracyPercent = 10;

    /// <summary>Silence after the port is opened at a new rate, before the first request is sent.</summary>
    public static readonly TimeSpan DefaultPortSettleDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>The expected baud rate and the frame format, which stays the same for the whole sweep.</summary>
    public required SerialSettings Serial { get; init; }

    /// <summary>Address of the slave to sweep. Address 0 (broadcast) is not allowed: the sweep needs replies.</summary>
    public required byte SlaveId
    {
        get;
        init
        {
            if (value == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(SlaveId), value, "Address 0 is broadcast and never answers.");
            }

            field = value;
        }
    }

    /// <summary>The request sent at every rate. Any valid reply counts, so an exception reply is fine.</summary>
    public required ProbeRequest Probe { get; init; }

    public required IBaudSweepStrategy Strategy { get; init; }

    /// <summary>How far from the expected rate the sweep may go, in percent, on each side.</summary>
    public double SpanPercent
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, BaudGrid.MaxSpanPercent);
            field = value;
        }
    } = DefaultSpanPercent;

    /// <summary>Distance between neighbouring rates, in percent of the expected rate.</summary>
    public double StepPercent
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, BaudGrid.MinStepPercent);
            field = value;
        }
    } = DefaultStepPercent;

    /// <summary>
    /// How far past the last reply a sweep keeps going before it gives up on that direction, in percent. It is also
    /// the first step of the staircase and the width of the edge passes.
    /// </summary>
    public double DeadBandPercent
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            field = value;
        }
    } = DefaultDeadBandPercent;

    /// <summary>Requests each rate gets; how they are spread over the sweep is up to the strategy.</summary>
    public int RequestsPerRate
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRequestsPerRate);
            field = value;
        }
    } = DefaultRequestsPerRate;

    /// <summary>
    /// Times each edge is swept again once the sweep proper is done, from half a dead band below it to half a dead
    /// band above it, to check that it has not moved in the meantime. Only strategies with a
    /// <see cref="IBaudSweepStrategy.VerifyEdgesNote"/> use it.
    /// </summary>
    public int VerifyEdgePasses
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxVerifyEdgePasses);
            field = value;
        }
    } = DefaultVerifyEdgePasses;

    /// <summary>
    /// Known error of the port's own clock, in percent, used to turn the measured middle of the window into a range
    /// for the device's rate. Null when it is unknown.
    /// </summary>
    public double? AdapterAccuracyPercent
    {
        get;
        init
        {
            if (value is double accuracy)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(accuracy);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(accuracy, MaxAdapterAccuracyPercent);
            }

            field = value;
        }
    }

    public TimeSpan ResponseTimeout
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            field = value;
        }
    } = SlaveScanOptions.DefaultResponseTimeout;

    /// <summary>Pause after each transaction before the next request is sent.</summary>
    public TimeSpan InterRequestDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = SlaveScanOptions.DefaultInterRequestDelay;

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
    } = SlaveScanOptions.DefaultMaxFlushDuration;

    /// <summary>
    /// Silence after the port is reopened at a new rate. Opening can disturb the line, and the device needs a quiet
    /// t3.5 to resynchronise before it will recognise the next request as the start of a frame.
    /// </summary>
    public TimeSpan PortSettleDelay
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = DefaultPortSettleDelay;

    public BaudGrid Grid => new(Serial.BaudRate, SpanPercent, Math.Min(StepPercent, SpanPercent));

    /// <summary>The dead band expressed in grid steps.</summary>
    public int DeadBandSteps => Grid.StepsIn(DeadBandPercent);

    /// <summary>The frame format of the sweep at another baud rate.</summary>
    public SerialSettings SerialAt(int baudRate) =>
        new(baudRate, Serial.DataBits, Serial.Parity, Serial.StopBits);

    public TimeSpan FrameGapAt(SerialSettings serial) => FrameGap ?? RtuTiming.DefaultFrameGap(serial);

    public TransactionTiming TimingAt(SerialSettings serial) =>
        new(serial, FrameGapAt(serial), InterRequestDelay, MaxFlushDuration);

    public RequestEstimate EstimateRequests() => Strategy.EstimateRequests(this);

    /// <summary>
    /// Estimated duration if no request is answered: every request waits for the request to be sent, the full
    /// timeout, one frame gap of flushing and the inter-request delay, and every rate costs a settle delay on top.
    /// Reopening the port for each rate and browser timer overhead come on top of that, so real sweeps take longer.
    /// </summary>
    public TimeSpan EstimateWorstCaseDuration()
    {
        var requests = EstimateRequests().Maximum;
        var serial = SerialAt(Grid.LowestBaudRate);
        var transmission = RtuTiming.TransmissionTime(serial, Probe.BuildFrame(SlaveId).Length);
        var perRequest = transmission + ResponseTimeout + FrameGapAt(serial) + InterRequestDelay;
        return (perRequest + PortSettleDelay) * requests;
    }
}
