using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

public enum SweepOutcome
{
    Completed,
    Cancelled,
    Failed,
}

/// <param name="Requests">Requests sent so far.</param>
/// <param name="ExpectedRequests">Requests the sweep now expects to send in total; an estimate, not a promise.</param>
/// <param name="TestedRates">Rates that have had at least one request.</param>
/// <param name="EstimatedRemaining">Moving-average estimate for the requests still to come; null before the first one.</param>
public sealed record BaudSweepProgress(int Requests, int ExpectedRequests, int TestedRates, TimeSpan? EstimatedRemaining)
{
    public int RemainingRequests => Math.Max(ExpectedRequests - Requests, 0);
}

public abstract record BaudSweepEvent;

/// <summary>The port is about to be opened at <paramref name="Step"/>'s rate.</summary>
public sealed record BaudRateStartedEvent(BaudSweepStep Step) : BaudSweepEvent;

/// <summary>The port could not be opened at this rate; the adapter or its driver refused it.</summary>
public sealed record BaudRateRefusedEvent(int BaudRate, string Error) : BaudSweepEvent;

/// <summary>One request at one rate is finished, including flushing and the inter-request delay.</summary>
/// <param name="Duration">Time the transaction took; pauses happen between transactions and are not included.</param>
public sealed record BaudRequestCompletedEvent(int BaudRate, ProbeAttempt Attempt, TimeSpan Duration) : BaudSweepEvent;

/// <summary>Everything planned for one rate is done and the port is closed again.</summary>
/// <param name="ExpectedRequests">The planner's refreshed estimate of the total; null while it cannot tell.</param>
public sealed record BaudRateCompletedEvent(int BaudRate, int? ExpectedRequests) : BaudSweepEvent;

/// <summary>The strategy has nothing left to try. Cancellation and failures surface as exceptions instead.</summary>
public sealed record BaudSweepFinishedEvent : BaudSweepEvent;
