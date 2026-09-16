namespace ModbusTools.Core.Scanning;

/// <summary>Lets a caller pause and resume a long-running operation that observes the matching <see cref="PauseToken"/>.</summary>
public sealed class PauseTokenSource
{
    private readonly Lock gate = new();
    private TaskCompletionSource? resumed;

    public bool IsPaused
    {
        get
        {
            lock (gate)
            {
                return resumed is not null;
            }
        }
    }

    public PauseToken Token => new(this);

    public void Pause()
    {
        lock (gate)
        {
            resumed ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        TaskCompletionSource? toComplete;
        lock (gate)
        {
            toComplete = resumed;
            resumed = null;
        }

        toComplete?.TrySetResult();
    }

    internal Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return resumed is null ? Task.CompletedTask : resumed.Task.WaitAsync(cancellationToken);
        }
    }
}

/// <summary>Observed by an operation at safe points to honour pause requests. The default token never pauses.</summary>
public readonly struct PauseToken(PauseTokenSource? source)
{
    public static PauseToken None => default;

    public bool IsPaused => source?.IsPaused ?? false;

    /// <summary>Completes immediately when not paused, otherwise when resumed or cancelled.</summary>
    public Task WaitWhilePausedAsync(CancellationToken cancellationToken = default) =>
        source?.WaitWhilePausedAsync(cancellationToken) ?? Task.CompletedTask;
}
