namespace ModbusTools.Core.Serial;

/// <summary>Modbus RTU timing derived from the serial settings.</summary>
public static class RtuTiming
{
    /// <summary>Above this baud rate the specification fixes t3.5 instead of deriving it from the character time.</summary>
    public const int FixedTimingBaudThreshold = 19200;

    /// <summary>Fixed t3.5 used above <see cref="FixedTimingBaudThreshold"/>.</summary>
    public static readonly TimeSpan FixedInterFrameDelay = TimeSpan.FromMilliseconds(1.75);

    /// <summary>
    /// Lower bound for the frame gap used to detect the end of a received frame.
    /// </summary>
    /// <remarks>
    /// On the wire an RTU frame ends after 3.5 characters of silence, which is only a few milliseconds at common
    /// baud rates. Received bytes do not reach the application with that precision: USB-RS485 adapters batch them
    /// (the FTDI latency timer defaults to 16 ms), the OS driver and the browser's Web Serial stream add their own
    /// buffering, and browser timers are clamped to a few milliseconds. A single reply therefore often arrives as
    /// several chunks separated by gaps far longer than t3.5. Using strict t3.5 would split those replies into
    /// fragments that all look garbled, so the gap is never allowed below this value.
    /// </remarks>
    public static readonly TimeSpan MinimumFrameGap = TimeSpan.FromMilliseconds(20);

    /// <summary>Time to transmit one character: (start + data + parity + stop bits) / baud.</summary>
    public static TimeSpan CharacterTime(SerialSettings settings) =>
        TimeSpan.FromSeconds((double)settings.BitsPerCharacter / settings.BaudRate);

    /// <summary>Nominal RTU inter-frame silence t3.5.</summary>
    public static TimeSpan InterFrameDelay(SerialSettings settings) =>
        settings.BaudRate > FixedTimingBaudThreshold
            ? FixedInterFrameDelay
            : CharacterTime(settings) * 3.5;

    /// <summary>Default silence that ends a received frame: max(t3.5, <see cref="MinimumFrameGap"/>).</summary>
    public static TimeSpan DefaultFrameGap(SerialSettings settings)
    {
        var interFrameDelay = InterFrameDelay(settings);
        return interFrameDelay > MinimumFrameGap ? interFrameDelay : MinimumFrameGap;
    }

    /// <summary>Estimated time for <paramref name="byteCount"/> bytes to leave the transmitter.</summary>
    public static TimeSpan TransmissionTime(SerialSettings settings, int byteCount) =>
        CharacterTime(settings) * byteCount;
}
