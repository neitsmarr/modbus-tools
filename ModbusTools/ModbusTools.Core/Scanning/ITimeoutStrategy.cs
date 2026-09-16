namespace ModbusTools.Core.Scanning;

/// <summary>Decides how long to wait for a response to each probe attempt.</summary>
public interface ITimeoutStrategy
{
    /// <param name="slaveId">The address being probed.</param>
    /// <param name="attemptNumber">1 for the first attempt, 2 for the first retry, and so on.</param>
    TimeSpan GetResponseTimeout(byte slaveId, int attemptNumber);

    /// <summary>Called after every attempt, so adaptive strategies can learn from observed response times.</summary>
    void OnAttemptCompleted(byte slaveId, ProbeAttempt attempt)
    {
    }
}

public sealed record FixedTimeoutStrategy : ITimeoutStrategy
{
    public FixedTimeoutStrategy(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }

    public TimeSpan GetResponseTimeout(byte slaveId, int attemptNumber) => Timeout;
}
