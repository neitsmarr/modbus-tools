using Microsoft.JSInterop;
using ModbusTools.Core.Serial;
using ModbusTools.Core.Transport;

namespace ModbusTools.Browser;

/// <summary>
/// <see cref="IModbusRtuTransport"/> over a Web Serial port, driven entirely through JS interop calls on the port's
/// stream objects.
/// </summary>
/// <remarks>
/// A Web Serial <c>reader.read()</c> cannot time out without cancelling the whole stream, so a read that times out
/// is kept pending and handed to the next <see cref="ReadAsync"/> call; no received bytes are lost.
/// </remarks>
public sealed class WebSerialTransport(WebSerialPort port, TimeProvider timeProvider) : IModbusRtuTransport
{
    // Polling interval while draining already-received input; each read resolves after a JS round trip.
    private static readonly TimeSpan DrainPollInterval = TimeSpan.FromMilliseconds(1);

    // Bounds draining so a line that never goes quiet (e.g. a device streaming at another baud rate) cannot stall.
    private static readonly TimeSpan MaxDrainDuration = TimeSpan.FromMilliseconds(100);

    private const int ReceiveBufferSize = 4096;

    private bool portOpened;
    private IJSObjectReference? reader;
    private IJSObjectReference? writer;
    private Task<ReadResult>? pendingRead;

    public bool IsOpen => reader is not null && writer is not null;

    /// <inheritdoc />
    public int LineErrorCount { get; private set; }

    public async ValueTask OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (portOpened)
        {
            throw new InvalidOperationException("The port is already open.");
        }

        await port.Handle.InvokeVoidAsync("open", cancellationToken, new
        {
            baudRate = settings.BaudRate,
            dataBits = settings.DataBits,
            stopBits = (int)settings.StopBits,
            parity = settings.Parity.ToString().ToLowerInvariant(),
            bufferSize = ReceiveBufferSize,
        });
        portOpened = true;

        try
        {
            await using (var readable = await port.Handle.GetValueAsync<IJSObjectReference>("readable", cancellationToken))
            {
                reader = await readable.InvokeAsync<IJSObjectReference>("getReader", cancellationToken);
            }

            await using (var writable = await port.Handle.GetValueAsync<IJSObjectReference>("writable", cancellationToken))
            {
                writer = await writable.InvokeAsync<IJSObjectReference>("getWriter", cancellationToken);
            }
        }
        catch
        {
            await CloseAsync();
            throw;
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var activeWriter = writer ?? throw new InvalidOperationException("The port is not open.");
        await activeWriter.InvokeVoidAsync("write", cancellationToken, data.ToArray());
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = timeProvider.GetTimestamp() +
            (long)(Math.Max(timeout.TotalSeconds, 0) * timeProvider.TimestampFrequency);

        while (true)
        {
            var activeReader = reader ?? throw new InvalidOperationException("The port is not open.");
            pendingRead ??= activeReader.InvokeAsync<ReadResult>("read").AsTask();

            if (!pendingRead.IsCompleted)
            {
                using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delay = Task.Delay(RemainingUntil(deadline), timeProvider, delayCancellation.Token);
                var completed = await Task.WhenAny(pendingRead, delay);
                await delayCancellation.CancelAsync();
                if (completed != pendingRead)
                {
                    // Surfaces cancellation of the caller's token; otherwise the timeout simply elapsed.
                    cancellationToken.ThrowIfCancellationRequested();
                    return ReadOnlyMemory<byte>.Empty;
                }
            }

            var read = pendingRead;
            pendingRead = null;
            ReadResult result;
            try
            {
                result = await read;
            }
            catch (JSException ex)
            {
                // A line error (framing, parity, break, buffer overrun) errors the stream but leaves the port open;
                // the browser replaces it with a fresh one. Keep waiting on that one for the rest of the timeout, so
                // a garbled reply still ends up as received bytes rather than as a failed transport.
                if (!await TryReplaceReaderAsync())
                {
                    throw new IOException($"The serial port's input stream failed: {JsErrors.FirstLine(ex)}", ex);
                }

                LineErrorCount++;
                if (RemainingUntil(deadline) <= TimeSpan.Zero)
                {
                    return ReadOnlyMemory<byte>.Empty;
                }

                continue;
            }

            if (result.Done)
            {
                throw new IOException("The serial port's input stream was closed.");
            }

            return result.Value ?? ReadOnlyMemory<byte>.Empty;
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>> DiscardInputAsync(CancellationToken cancellationToken = default)
    {
        var discarded = new List<byte>();
        var start = timeProvider.GetTimestamp();
        while (timeProvider.GetElapsedTime(start) < MaxDrainDuration)
        {
            var chunk = await ReadAsync(DrainPollInterval, cancellationToken);
            if (chunk.IsEmpty)
            {
                break;
            }

            discarded.AddRange(chunk.Span);
        }

        return discarded.ToArray();
    }

    public async ValueTask CloseAsync()
    {
        if (reader is not null)
        {
            await IgnoreJsErrorsAsync(() => reader.InvokeVoidAsync("cancel"));
            if (pendingRead is not null)
            {
                // Cancelling resolves the pending read; a lost device rejects it instead.
                await IgnoreJsErrorsAsync(async () => await pendingRead);
                pendingRead = null;
            }

            await IgnoreJsErrorsAsync(() => reader.InvokeVoidAsync("releaseLock"));
            await reader.DisposeAsync();
            reader = null;
        }

        if (writer is not null)
        {
            await IgnoreJsErrorsAsync(() => writer.InvokeVoidAsync("releaseLock"));
            await writer.DisposeAsync();
            writer = null;
        }

        if (portOpened)
        {
            await IgnoreJsErrorsAsync(() => port.Handle.InvokeVoidAsync("close"));
            portOpened = false;
        }
    }

    public ValueTask DisposeAsync() => CloseAsync();

    /// <summary>
    /// Takes a reader from the stream the browser puts in place of one that a non-fatal error killed. Returns false
    /// when the port cannot be read any more, which is how a fatal error (an unplugged device) shows up; the caller
    /// then reports the original error instead.
    /// </summary>
    private async ValueTask<bool> TryReplaceReaderAsync()
    {
        if (reader is not null)
        {
            await IgnoreJsErrorsAsync(() => reader.InvokeVoidAsync("releaseLock"));
            await reader.DisposeAsync();
            reader = null;
        }

        try
        {
            await using var readable = await port.Handle.GetValueAsync<IJSObjectReference?>("readable");
            if (readable is null)
            {
                return false;
            }

            reader = await readable.InvokeAsync<IJSObjectReference>("getReader");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private TimeSpan RemainingUntil(long timestamp)
    {
        var now = timeProvider.GetTimestamp();
        return now >= timestamp ? TimeSpan.Zero : timeProvider.GetElapsedTime(now, timestamp);
    }

    private static async ValueTask IgnoreJsErrorsAsync(Func<ValueTask> action)
    {
        try
        {
            await action();
        }
        catch (JSException)
        {
            // The port may already be closed or the device unplugged; closing must not fail.
        }
    }

    /// <summary>Result of <c>ReadableStreamDefaultReader.read()</c>.</summary>
    private sealed record ReadResult(byte[]? Value, bool Done);
}
