namespace ModbusTools.Core.Sniffing;

/// <summary>
/// Cuts the received byte stream into frames. This is the only part of the sniffer that depends on the framing: RTU
/// (binary with a CRC) now, ASCII (':' to CRLF with an LRC) later. Everything after it works on the frames.
/// </summary>
public interface IFrameSplitter
{
    /// <summary>Adds the bytes of one chunk and returns what they complete, in order.</summary>
    IReadOnlyList<SplitFrame> Push(ReadOnlySpan<byte> bytes, ChunkStamp stamp);

    /// <summary>
    /// The line went silent, or a line error broke the stream: nothing that follows can continue the bytes held back.
    /// Returns them as frames or garbled bytes; afterwards nothing is held back.
    /// </summary>
    IReadOnlyList<SplitFrame> Flush();
}

public enum SplitFrameKind
{
    /// <summary>A frame whose checksum is valid.</summary>
    Frame,

    /// <summary>Bytes that are not part of any valid frame.</summary>
    Garbled,
}

/// <summary>Directions a frame can travel in, judging by its content alone.</summary>
[Flags]
public enum FrameRoles
{
    None = 0,
    Request = 1,
    Response = 2,
    Either = Request | Response,
}

/// <param name="Bytes">The whole frame, address through checksum; or the garbled bytes.</param>
/// <param name="Roles">
/// For a frame, the directions its content fits. Both when a request and a response of this function would have the
/// same length, or when the function is not decoded; the frames around it then decide.
/// </param>
/// <param name="First">Chunk that brought the first byte.</param>
/// <param name="Last">Chunk that brought the last byte.</param>
public sealed record SplitFrame(SplitFrameKind Kind, byte[] Bytes, FrameRoles Roles, ChunkStamp First, ChunkStamp Last);
