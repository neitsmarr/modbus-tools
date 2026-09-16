using System.Runtime.InteropServices;
using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Transport;

/// <param name="Bytes">All bytes that belong to the response; empty if nothing arrived before the deadline.</param>
/// <param name="FirstByteTimestamp">
/// <see cref="TimeProvider.GetTimestamp"/> when the first chunk was received, or null if nothing arrived.
/// </param>
public sealed record ReceivedFrame(ReadOnlyMemory<byte> Bytes, long? FirstByteTimestamp)
{
    public static ReceivedFrame Empty { get; } = new(ReadOnlyMemory<byte>.Empty, null);
}

/// <summary>Reads RTU frames from a transport, using silence on the line to detect where a frame ends.</summary>
public sealed class RtuFrameReader(IModbusRtuTransport transport, TimeProvider timeProvider)
{
    /// <summary>Maximum RTU frame size. Reading stops here so a continuous stream of noise cannot stall a probe.</summary>
    public const int MaxFrameLength = 256;

    /// <summary>
    /// Waits until <paramref name="deadlineTimestamp"/> for the first byte of a response, then collects bytes until
    /// the line is silent for <paramref name="frameGap"/>, the length predicted by <paramref name="probe"/> has been
    /// reached, or <see cref="MaxFrameLength"/> bytes have arrived. A frame that has started is always finished,
    /// even past the deadline.
    /// </summary>
    public async Task<ReceivedFrame> ReadResponseAsync(
        ProbeRequest probe, long deadlineTimestamp, TimeSpan frameGap, CancellationToken cancellationToken = default)
    {
        var first = await transport.ReadAsync(RemainingUntil(deadlineTimestamp), cancellationToken);
        if (first.IsEmpty)
        {
            return ReceivedFrame.Empty;
        }

        var firstByteTimestamp = timeProvider.GetTimestamp();
        var buffer = new List<byte>(MaxFrameLength);
        buffer.AddRange(first.Span);

        while (buffer.Count < MaxFrameLength)
        {
            if (probe.PredictResponseLength(CollectionsMarshal.AsSpan(buffer)) is int expected && buffer.Count >= expected)
            {
                break;
            }

            var chunk = await transport.ReadAsync(frameGap, cancellationToken);
            if (chunk.IsEmpty)
            {
                break;
            }

            buffer.AddRange(chunk.Span);
        }

        return new ReceivedFrame(buffer.ToArray(), firstByteTimestamp);
    }

    /// <summary>
    /// Collects bytes until the line has been silent for <paramref name="frameGap"/>, giving up after
    /// <paramref name="maxDuration"/>. Used to flush late or stray bytes so they are not taken as the next response.
    /// </summary>
    public async Task<ReadOnlyMemory<byte>> ReadUntilSilentAsync(
        TimeSpan frameGap, TimeSpan maxDuration, CancellationToken cancellationToken = default)
    {
        var start = timeProvider.GetTimestamp();
        var buffer = new List<byte>();
        while (true)
        {
            var left = maxDuration - timeProvider.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                break;
            }

            var chunk = await transport.ReadAsync(left < frameGap ? left : frameGap, cancellationToken);
            if (chunk.IsEmpty)
            {
                break;
            }

            buffer.AddRange(chunk.Span);
        }

        return buffer.ToArray();
    }

    private TimeSpan RemainingUntil(long timestamp)
    {
        var now = timeProvider.GetTimestamp();
        return now >= timestamp ? TimeSpan.Zero : timeProvider.GetElapsedTime(now, timestamp);
    }
}
