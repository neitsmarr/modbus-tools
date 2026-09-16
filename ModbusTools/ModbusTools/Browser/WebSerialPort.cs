using Microsoft.JSInterop;

namespace ModbusTools.Browser;

/// <summary>
/// A serial port the user has granted this site access to. Browsers do not expose OS port names such as COM3,
/// so a port is identified by its USB vendor/product ID and its position among granted ports with the same IDs.
/// </summary>
public sealed class WebSerialPort
{
    internal WebSerialPort(IJSObjectReference handle, SerialPortInfo info)
    {
        Handle = handle;
        Info = info;
    }

    /// <summary>Stable identifier suitable for URLs, e.g. "0403:6001#1".</summary>
    public string Id { get; private set; } = string.Empty;

    /// <summary>Human-readable name, e.g. "USB 0403:6001 (#1)".</summary>
    public string DisplayName { get; private set; } = string.Empty;

    internal IJSObjectReference Handle { get; }

    internal SerialPortInfo Info { get; }

    internal string DeviceKey => Info.UsbVendorId is int vendor && Info.UsbProductId is int product
        ? $"{vendor:X4}:{product:X4}"
        : "serial";

    internal void AssignOrdinal(int ordinal)
    {
        Id = $"{DeviceKey}#{ordinal}";
        DisplayName = Info.UsbVendorId is null
            ? $"Serial port (#{ordinal})"
            : $"USB {DeviceKey} (#{ordinal})";
    }
}

/// <summary>Result of the Web Serial <c>SerialPort.getInfo()</c> call.</summary>
internal sealed record SerialPortInfo(int? UsbVendorId, int? UsbProductId);
