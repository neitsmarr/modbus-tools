using ModbusTools.Components.Serial;
using ModbusTools.Core.BaudSweeping;
using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;
using ModbusTools.Core.Serial;
using ModbusTools.Scanner;

namespace ModbusTools.Sweep;

/// <summary>Editable state of the sweep configuration form; <see cref="Validate"/> turns it into sweep options.</summary>
public sealed class SweepFormModel : ISerialSettingsForm
{
    /// <summary>Rates the chart and the table can still show, and a sweep can still finish, in one run.</summary>
    public const int MaxRates = 2001;

    public const int MaxPortSettleMs = 10_000;

    /// <summary>The expected baud rate: the middle of the sweep.</summary>
    public int BaudRate { get; set; } = 19200;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.Even;

    public StopBits StopBits { get; set; } = StopBits.One;

    public int SlaveId { get; set; } = SlaveIdRange.FirstId;

    public FunctionCode FunctionCode { get; set; } = FunctionCode.ReadHoldingRegisters;

    /// <summary>Start address, decimal or 0x-prefixed hex.</summary>
    public string AddressText { get; set; } = "0";

    public int Quantity { get; set; } = 1;

    public string StrategyId { get; set; } = BaudSweepStrategies.Default.Id;

    public double SpanPercent { get; set; } = BaudSweepOptions.DefaultSpanPercent;

    public double StepPercent { get; set; } = BaudSweepOptions.DefaultStepPercent;

    public double DeadBandPercent { get; set; } = BaudSweepOptions.DefaultDeadBandPercent;

    /// <summary>Whether to use the dead band; only strategies for which it is optional let it be turned off.</summary>
    public bool UseDeadBand { get; set; } = true;

    public int RequestsPerRate { get; set; } = BaudSweepOptions.DefaultRequestsPerRate;

    /// <summary>Whether to sweep the edges again; only used by strategies that verify edges.</summary>
    public bool VerifyEdges { get; set; } = true;

    /// <summary>How many times each edge is swept again when <see cref="VerifyEdges"/> is on.</summary>
    public int VerifyEdgePasses { get; set; } = BaudSweepOptions.DefaultVerifyEdgePasses;

    /// <summary>Known error of the port's own clock, in percent; empty when unknown.</summary>
    public double? AdapterAccuracyPercent { get; set; }

    public int ResponseTimeoutMs { get; set; } = (int)SlaveScanOptions.DefaultResponseTimeout.TotalMilliseconds;

    public int InterRequestDelayMs { get; set; } = (int)SlaveScanOptions.DefaultInterRequestDelay.TotalMilliseconds;

    /// <summary>Frame gap override in milliseconds; null uses max(t3.5, 20 ms).</summary>
    public double? FrameGapMs { get; set; }

    public int PortSettleMs { get; set; } = (int)BaudSweepOptions.DefaultPortSettleDelay.TotalMilliseconds;

    public int MaxQuantity => FunctionCode is FunctionCode.ReadCoils or FunctionCode.ReadDiscreteInputs
        ? ReadBitsProbe.MaxQuantity
        : ReadRegistersProbe.MaxQuantity;

    /// <summary>Prefills settings passed from another tool. Missing or invalid values are ignored.</summary>
    public void ApplyQuery(int? baudRate, int? dataBits, string? parity, int? stopBits, int? slaveId)
    {
        if (baudRate > 0)
        {
            BaudRate = baudRate.Value;
        }

        if (dataBits is 7 or 8)
        {
            DataBits = dataBits.Value;
        }

        Parity = parity?.ToUpperInvariant() switch
        {
            "N" => Parity.None,
            "E" => Parity.Even,
            "O" => Parity.Odd,
            _ => Parity,
        };

        if (stopBits is 1 or 2)
        {
            StopBits = (StopBits)stopBits.Value;
        }

        if (slaveId is >= SlaveIdRange.FirstId and <= SlaveIdRange.LastExtendedId)
        {
            SlaveId = slaveId.Value;
        }
    }

    public SweepFormValidation Validate()
    {
        var errors = new Dictionary<string, string>();
        var serial = ISerialSettingsForm.Validate(this, errors);

        if (SlaveId is < SlaveIdRange.FirstId or > SlaveIdRange.LastExtendedId)
        {
            errors[nameof(SlaveId)] = $"Slave ID must be between {SlaveIdRange.FirstId} and {SlaveIdRange.LastExtendedId}.";
        }

        var probe = BuildProbe(errors);
        var strategy = BaudSweepStrategies.Find(StrategyId);
        if (strategy is null)
        {
            errors[nameof(StrategyId)] = "Select a search method.";
        }

        var grid = ValidateGrid(serial, errors);
        var verifiesEdges = VerifyEdges && strategy?.VerifyEdgesNote is not null;
        if (verifiesEdges && VerifyEdgePasses is < 1 or > BaudSweepOptions.MaxVerifyEdgePasses)
        {
            errors[nameof(VerifyEdgePasses)] = $"Edge passes must be between 1 and {BaudSweepOptions.MaxVerifyEdgePasses}.";
        }

        ValidateTiming(errors);

        BaudSweepOptions? options = null;
        if (errors.Count == 0)
        {
            options = new BaudSweepOptions
            {
                Serial = serial!,
                SlaveId = (byte)SlaveId,
                Probe = probe!,
                Strategy = strategy!,
                SpanPercent = SpanPercent,
                StepPercent = StepPercent,
                DeadBandPercent = DeadBandPercent,
                UseDeadBand = UseDeadBand || strategy!.DeadBandNote is null,
                RequestsPerRate = RequestsPerRate,
                VerifyEdgePasses = verifiesEdges ? VerifyEdgePasses : 0,
                AdapterAccuracyPercent = AdapterAccuracyPercent,
                ResponseTimeout = TimeSpan.FromMilliseconds(ResponseTimeoutMs),
                InterRequestDelay = TimeSpan.FromMilliseconds(InterRequestDelayMs),
                FrameGap = FrameGapMs is double frameGap ? TimeSpan.FromMilliseconds(frameGap) : null,
                PortSettleDelay = TimeSpan.FromMilliseconds(PortSettleMs),
            };
        }

        return new SweepFormValidation(errors, serial, grid, probe, options);
    }

    private BaudGrid? ValidateGrid(SerialSettings? serial, Dictionary<string, string> errors)
    {
        if (SpanPercent is <= 0 or > BaudGrid.MaxSpanPercent)
        {
            errors[nameof(SpanPercent)] = $"Span must be above 0 and at most {BaudGrid.MaxSpanPercent:0} %.";
        }

        if (StepPercent < BaudGrid.MinStepPercent || StepPercent > SpanPercent)
        {
            errors[nameof(StepPercent)] = $"Step must be between {BaudGrid.MinStepPercent:0.##} % and the span.";
        }

        if (DeadBandPercent <= 0 || DeadBandPercent > SpanPercent)
        {
            errors[nameof(DeadBandPercent)] = "Dead band must be above 0 and at most the span.";
        }

        if (RequestsPerRate is < 1 or > BaudSweepOptions.MaxRequestsPerRate)
        {
            errors[nameof(RequestsPerRate)] = $"Requests per rate must be between 1 and {BaudSweepOptions.MaxRequestsPerRate}.";
        }

        if (AdapterAccuracyPercent is double accuracy &&
            (accuracy < 0 || accuracy > BaudSweepOptions.MaxAdapterAccuracyPercent))
        {
            errors[nameof(AdapterAccuracyPercent)] =
                $"Accuracy must be between 0 and {BaudSweepOptions.MaxAdapterAccuracyPercent:0} %, or empty.";
        }

        if (serial is null || errors.ContainsKey(nameof(SpanPercent)) || errors.ContainsKey(nameof(StepPercent)))
        {
            return null;
        }

        var grid = new BaudGrid(serial.BaudRate, SpanPercent, StepPercent);
        if (grid.Count > MaxRates)
        {
            errors[nameof(StepPercent)] = $"That is {grid.Count} rates; keep it under {MaxRates} with a larger step or a smaller span.";
            return null;
        }

        return grid;
    }

    private void ValidateTiming(Dictionary<string, string> errors)
    {
        if (ResponseTimeoutMs is < 1 or > ScanFormModel.MaxResponseTimeoutMs)
        {
            errors[nameof(ResponseTimeoutMs)] = $"Timeout must be between 1 and {ScanFormModel.MaxResponseTimeoutMs} ms.";
        }

        if (InterRequestDelayMs is < 0 or > ScanFormModel.MaxInterRequestDelayMs)
        {
            errors[nameof(InterRequestDelayMs)] = $"Delay must be between 0 and {ScanFormModel.MaxInterRequestDelayMs} ms.";
        }

        if (FrameGapMs is double gap && (gap <= 0 || gap > ScanFormModel.MaxFrameGapMs))
        {
            errors[nameof(FrameGapMs)] = $"Frame gap must be above 0 and at most {ScanFormModel.MaxFrameGapMs} ms, or empty for automatic.";
        }

        if (PortSettleMs is < 0 or > MaxPortSettleMs)
        {
            errors[nameof(PortSettleMs)] = $"Settle time must be between 0 and {MaxPortSettleMs} ms.";
        }
    }

    private ProbeRequest? BuildProbe(Dictionary<string, string> errors)
    {
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

/// <summary>Validation outcome of <see cref="SweepFormModel"/>; the parts that are valid are available for previews.</summary>
public sealed record SweepFormValidation(
    IReadOnlyDictionary<string, string> Errors,
    SerialSettings? Serial,
    BaudGrid? Grid,
    ProbeRequest? Probe,
    BaudSweepOptions? Options)
{
    public bool IsValid => Options is not null;

    public string? ErrorFor(string field) => Errors.GetValueOrDefault(field);
}
