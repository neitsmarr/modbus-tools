using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;
using ModbusTools.Core.Serial;

namespace ModbusTools.Scanner;

/// <summary>Editable state of the scan configuration form; <see cref="Validate"/> turns it into scan options.</summary>
public sealed class ScanFormModel
{
    public const int MaxResponseTimeoutMs = 60_000;
    public const int MaxInterRequestDelayMs = 10_000;
    public const int MaxFrameGapMs = 1_000;

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public int FromId { get; set; } = SlaveIdRange.FirstId;

    public int ToId { get; set; } = SlaveIdRange.LastStandardId;

    public bool IncludeReservedIds { get; set; }

    public FunctionCode FunctionCode { get; set; } = FunctionCode.ReadHoldingRegisters;

    /// <summary>Start address, decimal or 0x-prefixed hex.</summary>
    public string AddressText { get; set; } = "0";

    public int Quantity { get; set; } = 1;

    public ReadDeviceIdCode ReadDeviceIdCode { get; set; } = ReadDeviceIdCode.Basic;

    /// <summary>Object ID for FC43/14, decimal or 0x-prefixed hex.</summary>
    public string ObjectIdText { get; set; } = "0";

    public int ResponseTimeoutMs { get; set; } = (int)SlaveScanOptions.DefaultResponseTimeout.TotalMilliseconds;

    public int Retries { get; set; }

    public int InterRequestDelayMs { get; set; } = (int)SlaveScanOptions.DefaultInterRequestDelay.TotalMilliseconds;

    /// <summary>Frame gap override in milliseconds; null uses max(t3.5, 20 ms).</summary>
    public double? FrameGapMs { get; set; }

    public bool StopAtFirstFound { get; set; }

    public bool UsesAddressAndQuantity => FunctionCode is FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs
        or FunctionCode.ReadHoldingRegisters or FunctionCode.ReadInputRegisters;

    public int MaxQuantity => FunctionCode is FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs
        ? ReadBitsProbe.MaxQuantity
        : ReadRegistersProbe.MaxQuantity;

    public ScanFormValidation Validate()
    {
        var errors = new Dictionary<string, string>();

        SerialSettings? serial = null;
        if (BaudRate <= 0)
        {
            errors[nameof(BaudRate)] = "Baud rate must be positive.";
        }
        else
        {
            serial = new SerialSettings(BaudRate, DataBits, Parity, StopBits);
        }

        SlaveIdRange? range = null;
        if (SlaveIdRange.TryCreate(FromId, ToId, IncludeReservedIds, out var validRange, out var rangeError))
        {
            range = validRange;
        }
        else
        {
            errors[nameof(SlaveIdRange)] = rangeError!;
        }

        var probe = BuildProbe(errors);

        if (ResponseTimeoutMs is < 1 or > MaxResponseTimeoutMs)
        {
            errors[nameof(ResponseTimeoutMs)] = $"Timeout must be between 1 and {MaxResponseTimeoutMs} ms.";
        }

        if (Retries is < 0 or > SlaveScanOptions.MaxRetries)
        {
            errors[nameof(Retries)] = $"Retries must be between 0 and {SlaveScanOptions.MaxRetries}.";
        }

        if (InterRequestDelayMs is < 0 or > MaxInterRequestDelayMs)
        {
            errors[nameof(InterRequestDelayMs)] = $"Delay must be between 0 and {MaxInterRequestDelayMs} ms.";
        }

        if (FrameGapMs is double gap && (gap <= 0 || gap > MaxFrameGapMs))
        {
            errors[nameof(FrameGapMs)] = $"Frame gap must be above 0 and at most {MaxFrameGapMs} ms, or empty for automatic.";
        }

        SlaveScanOptions? options = null;
        if (errors.Count == 0)
        {
            options = new SlaveScanOptions
            {
                Serial = serial!,
                SlaveIds = range!.Value.ToIdList(),
                Probe = probe!,
                TimeoutStrategy = new FixedTimeoutStrategy(TimeSpan.FromMilliseconds(ResponseTimeoutMs)),
                Retries = Retries,
                InterRequestDelay = TimeSpan.FromMilliseconds(InterRequestDelayMs),
                FrameGap = FrameGapMs is double frameGap ? TimeSpan.FromMilliseconds(frameGap) : null,
                StopAtFirstFound = StopAtFirstFound,
            };
        }

        return new ScanFormValidation(errors, serial, range, probe, options);
    }

    private ProbeRequest? BuildProbe(Dictionary<string, string> errors)
    {
        switch (FunctionCode)
        {
            case FunctionCode.ReportServerId:
                return new ReportServerIdProbe();

            case FunctionCode.EncapsulatedInterfaceTransport:
                if (!Hex.TryParseNumber(ObjectIdText, byte.MaxValue, out var objectId))
                {
                    errors[nameof(ObjectIdText)] = "Object ID must be 0-255 (decimal or 0x hex).";
                    return null;
                }

                return new ReadDeviceIdentificationProbe(ReadDeviceIdCode, (byte)objectId);
        }

        var valid = true;
        if (!Hex.TryParseNumber(AddressText, ushort.MaxValue, out var address))
        {
            errors[nameof(AddressText)] = "Address must be 0-65535 (decimal or 0x hex).";
            valid = false;
        }

        if (Quantity < 1 || Quantity > MaxQuantity)
        {
            errors[nameof(Quantity)] = $"Quantity must be between 1 and {MaxQuantity}.";
            valid = false;
        }
        else if (valid && address + Quantity > 0x10000)
        {
            errors[nameof(Quantity)] = "Address + quantity must not exceed 65536.";
            valid = false;
        }

        if (!valid)
        {
            return null;
        }

        return FunctionCode is FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs
            ? new ReadBitsProbe(FunctionCode, (ushort)address, (ushort)Quantity)
            : new ReadRegistersProbe(FunctionCode, (ushort)address, (ushort)Quantity);
    }
}

/// <summary>Validation outcome of <see cref="ScanFormModel"/>; the parts that are valid are available for previews.</summary>
public sealed record ScanFormValidation(
    IReadOnlyDictionary<string, string> Errors,
    SerialSettings? Serial,
    SlaveIdRange? Range,
    ProbeRequest? Probe,
    SlaveScanOptions? Options)
{
    public bool IsValid => Options is not null;

    public string? ErrorFor(string field) => Errors.GetValueOrDefault(field);
}
