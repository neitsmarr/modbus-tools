using System.Runtime.InteropServices;
using ModbusTools.Core.Protocol;
using ModbusTools.Core.Transport;

namespace ModbusTools.Core.Sniffing;

/// <summary>
/// Splits a Modbus RTU byte stream into frames by content rather than by timing.
/// </summary>
/// <remarks>
/// <para>
/// Silence on the line cannot be trusted to separate frames here: USB adapters and the browser deliver bytes in
/// batches (see <see cref="Serial.RtuTiming.MinimumFrameGap"/>), so a request and its reply often arrive in one chunk,
/// and one frame can arrive in several. Instead, the function code gives the lengths a request and a response of that
/// function can have (<see cref="PduLengthRules"/>), and the CRC tells which of them, if any, is a frame.
/// </para>
/// <para>
/// When no length fits, the first byte cannot start a frame: it is set aside as garbled and the search goes on from
/// the next one, which finds the start of the next valid frame again. Functions without length rules are only cut
/// where the line goes silent (<see cref="Flush"/>), and only if the whole run has a valid CRC.
/// </para>
/// </remarks>
public sealed class RtuFrameSplitter : IFrameSplitter
{
    private const int AddressLength = 1;
    private const int CrcLength = 2;

    /// <summary>Address, function code and CRC.</summary>
    private const int MinimumFrameLength = AddressLength + 1 + CrcLength;

    private readonly List<byte> pending = [];
    private readonly List<ChunkStamp> pendingStamps = [];
    private readonly List<byte> garbled = [];
    private ChunkStamp garbledFirst;
    private ChunkStamp garbledLast;

    private enum Decision
    {
        /// <summary>More bytes could still complete a frame.</summary>
        Wait,

        /// <summary>A frame of the given length starts here.</summary>
        Frame,

        /// <summary>No frame can start here.</summary>
        Reject,
    }

    public IReadOnlyList<SplitFrame> Push(ReadOnlySpan<byte> bytes, ChunkStamp stamp)
    {
        foreach (var value in bytes)
        {
            pending.Add(value);
            pendingStamps.Add(stamp);
        }

        var output = new List<SplitFrame>();
        Split(output, atEnd: false);
        return output;
    }

    public IReadOnlyList<SplitFrame> Flush()
    {
        var output = new List<SplitFrame>();
        Split(output, atEnd: true);
        EmitGarbled(output);
        return output;
    }

    private void Split(List<SplitFrame> output, bool atEnd)
    {
        var start = 0;
        while (start < pending.Count)
        {
            var bytes = CollectionsMarshal.AsSpan(pending)[start..];
            var (decision, length, roles) = Decide(bytes, atEnd);
            if (decision == Decision.Wait)
            {
                break;
            }

            if (decision == Decision.Frame)
            {
                EmitGarbled(output);
                output.Add(new SplitFrame(
                    SplitFrameKind.Frame, bytes[..length].ToArray(), roles, pendingStamps[start], pendingStamps[start + length - 1]));
                start += length;
            }
            else
            {
                if (garbled.Count == 0)
                {
                    garbledFirst = pendingStamps[start];
                }

                garbled.Add(bytes[0]);
                garbledLast = pendingStamps[start];
                start++;
            }
        }

        pending.RemoveRange(0, start);
        pendingStamps.RemoveRange(0, start);
    }

    private void EmitGarbled(List<SplitFrame> output)
    {
        if (garbled.Count == 0)
        {
            return;
        }

        output.Add(new SplitFrame(SplitFrameKind.Garbled, garbled.ToArray(), FrameRoles.None, garbledFirst, garbledLast));
        garbled.Clear();
    }

    /// <summary>Decides whether a frame starts at the beginning of <paramref name="bytes"/>.</summary>
    /// <param name="atEnd">True when no more bytes will join these, so waiting is not an option.</param>
    private static (Decision Decision, int Length, FrameRoles Roles) Decide(ReadOnlySpan<byte> bytes, bool atEnd)
    {
        if (bytes.Length <= AddressLength)
        {
            return (atEnd ? Decision.Reject : Decision.Wait, 0, FrameRoles.None);
        }

        var functionCode = bytes[AddressLength];
        if ((functionCode & FunctionCodes.ExceptionFlag) != 0)
        {
            var candidates = new Candidates();
            candidates.Try(bytes, PduLengthRules.ExceptionResponse, FrameRoles.Response);
            return candidates.Decide(atEnd);
        }

        if (PduLengthRules.TryGetRules(functionCode, out var request, out var response))
        {
            var candidates = new Candidates();
            candidates.Try(bytes, request, FrameRoles.Request);
            candidates.Try(bytes, response, FrameRoles.Response);
            return candidates.Decide(atEnd);
        }

        return DecideUndecoded(bytes, atEnd);
    }

    /// <summary>
    /// A function without length rules: the frame ends where the line goes silent, and the whole run must then be one
    /// frame. A run that reaches the longest possible frame without a silence cannot be one either.
    /// </summary>
    private static (Decision Decision, int Length, FrameRoles Roles) DecideUndecoded(ReadOnlySpan<byte> bytes, bool atEnd)
    {
        if (!atEnd && bytes.Length < RtuFrameReader.MaxFrameLength)
        {
            return (Decision.Wait, 0, FrameRoles.None);
        }

        var length = Math.Min(bytes.Length, RtuFrameReader.MaxFrameLength);
        return length >= MinimumFrameLength && ModbusCrc.IsValid(bytes[..length])
            ? (Decision.Frame, length, FrameRoles.Either)
            : (Decision.Reject, 0, FrameRoles.None);
    }

    /// <summary>The frame lengths the function allows, checked against the bytes received so far.</summary>
    private struct Candidates
    {
        private int bestLength;
        private FrameRoles bestRoles;
        private bool incomplete;

        /// <summary>Checks whether a frame of the length <paramref name="rule"/> gives starts here.</summary>
        public void Try(ReadOnlySpan<byte> bytes, PduLengthRule rule, FrameRoles role)
        {
            if (rule.GetLength(bytes[AddressLength..]) is not int pduLength)
            {
                // The byte count that decides the length has not arrived yet.
                incomplete = true;
                return;
            }

            var length = AddressLength + pduLength + CrcLength;
            if (length > RtuFrameReader.MaxFrameLength)
            {
                return;
            }

            if (bytes.Length < length)
            {
                incomplete = true;
                return;
            }

            if (!ModbusCrc.IsValid(bytes[..length]))
            {
                return;
            }

            // Both readings with the same length cover the same bytes, so the CRC confirms both. Two different
            // lengths with valid CRCs only happen by coincidence; the shorter one is found first either way.
            if (bestLength == 0 || length < bestLength)
            {
                bestLength = length;
                bestRoles = role;
            }
            else if (length == bestLength)
            {
                bestRoles |= role;
            }
        }

        public readonly (Decision Decision, int Length, FrameRoles Roles) Decide(bool atEnd)
        {
            if (bestLength > 0)
            {
                return (Decision.Frame, bestLength, bestRoles);
            }

            return incomplete && !atEnd
                ? (Decision.Wait, 0, FrameRoles.None)
                : (Decision.Reject, 0, FrameRoles.None);
        }
    }
}
