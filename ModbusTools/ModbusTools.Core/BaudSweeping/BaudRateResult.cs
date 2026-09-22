using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// What every request sent at one baud rate achieved. Rates are visited several times by most strategies, so the
/// counters accumulate over the whole sweep.
/// </summary>
public sealed class BaudRateResult
{
    private readonly int[] statusCounts = new int[Enum.GetValues<ReplyStatus>().Length];

    private TimeSpan responseTimeTotal;
    private int responseTimeSamples;

    public BaudRateResult(int baudRate, double offsetPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baudRate, 0);
        BaudRate = baudRate;
        OffsetPercent = offsetPercent;
    }

    public int BaudRate { get; }

    /// <summary>Distance from the expected rate, in percent.</summary>
    public double OffsetPercent { get; }

    public int Requests { get; private set; }

    public int Answered => CountOf(ReplyStatus.Answered);

    public int BadReplies => CountOf(ReplyStatus.BadReply);

    public int NoReplies => CountOf(ReplyStatus.NoReply);

    /// <summary>Line errors reported while probing this rate; see <see cref="Transport.IModbusRtuTransport.LineErrorCount"/>.</summary>
    public int LineErrors { get; private set; }

    /// <summary>Why the port could not be opened at this rate, or null. The adapter or driver refused the rate.</summary>
    public string? PortError { get; private set; }

    public bool IsPortRefused => PortError is not null;

    public double SuccessRate => Requests == 0 ? 0 : (double)Answered / Requests;

    /// <summary>
    /// Whether the link works here, by majority vote over every request sent at this rate, a tie counting as working.
    /// Half the requests is the threshold because near an edge the outcome depends on the bits in each frame, so
    /// rates there answer some of the time; the edge is where that crosses one in two.
    /// </summary>
    public bool IsWorking => Requests > 0 && Answered * 2 >= Requests;

    public TimeSpan? AverageResponseTime =>
        responseTimeSamples == 0 ? null : responseTimeTotal / responseTimeSamples;

    public int CountOf(ReplyStatus status) => statusCounts[(int)status];

    public void Record(ProbeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        Requests++;
        statusCounts[(int)attempt.GetReplyStatus()]++;
        LineErrors += attempt.LineErrors;
        if (attempt.Status.IsFound() && attempt.ResponseTime is TimeSpan responseTime)
        {
            responseTimeTotal += responseTime;
            responseTimeSamples++;
        }
    }

    /// <summary>Records that the port could not be opened at this rate. Keeps the first error; rates are retried.</summary>
    public void RecordPortError(string message) => PortError ??= message;
}
