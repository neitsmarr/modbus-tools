using ModbusTools.Core.Serial;

namespace ModbusTools.Core.Transport;

/// <summary>
/// Raw byte transport for a Modbus RTU bus. Implementations do no framing; frame boundaries are detected by the
/// caller from silence on the line.
/// </summary>
public interface IModbusRtuTransport : IAsyncDisposable
{
    bool IsOpen { get; }

    /// <summary>
    /// Non-fatal line errors (framing, parity, break, buffer overrun) reported since the port was opened. The bytes
    /// they affected are lost, but the port stays usable, so they are counted rather than thrown: a probe that times
    /// out with line errors means the device did answer and the answer could not be read.
    /// </summary>
    int LineErrorCount => 0;

    /// <summary>Opens the port with the given settings. Fails if it is already open.</summary>
    ValueTask OpenAsync(SerialSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Releases the port. Never throws; safe to call when not open.</summary>
    ValueTask CloseAsync();

    /// <summary>Queues <paramref name="data"/> for transmission. Completion does not mean the bytes are on the wire yet.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for received bytes and returns the next chunk, or an empty buffer if
    /// nothing arrived in time. A zero timeout returns only bytes that are already available.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Drops input that has already been received and returns it for diagnostics.</summary>
    ValueTask<ReadOnlyMemory<byte>> DiscardInputAsync(CancellationToken cancellationToken = default);
}
