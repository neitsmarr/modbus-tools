namespace ModbusTools.Core.Sniffing;

public enum CaptureChunkKind
{
    /// <summary>Bytes handed over by one read of the port.</summary>
    Data,

    /// <summary>
    /// The port reported a framing, parity, break or overrun error here. The bytes it affected are lost, so the
    /// stream is broken at this point.
    /// </summary>
    LineError,
}

/// <param name="Number">Position in the capture, counting from 1; line errors are numbered along with data.</param>
/// <param name="Time">When the chunk was received, measured from the start of the capture.</param>
public sealed record CaptureChunk(long Number, TimeSpan Time, CaptureChunkKind Kind, ReadOnlyMemory<byte> Bytes)
{
    public ChunkStamp Stamp => new(Number, Time);
}

/// <summary>Which chunk a byte arrived in, and when.</summary>
public readonly record struct ChunkStamp(long Chunk, TimeSpan Time);
