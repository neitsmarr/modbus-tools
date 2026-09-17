using System.Globalization;
using ModbusTools.Components.Serial;
using ModbusTools.Core.Crawling;
using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;
using ModbusTools.Core.Serial;
using ModbusTools.Scanner;

namespace ModbusTools.Crawler;

public enum AddressRangePreset
{
    First1000,
    First10000,
    Full,
    Custom,
}

/// <summary>Editable state of the crawl configuration form; <see cref="Validate"/> turns it into crawl options.</summary>
public sealed class CrawlFormModel : ISerialSettingsForm
{
    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public int SlaveId { get; set; } = SlaveIdRange.FirstId;

    /// <summary>Selected tables; crawled in <see cref="CrawlTables.All"/> order.</summary>
    public HashSet<CrawlTable> Tables { get; } = [CrawlTable.HoldingRegisters, CrawlTable.InputRegisters];

    public AddressRangePreset RangePreset { get; set; } = AddressRangePreset.First1000;

    /// <summary>Start of a custom range, decimal or 0x-prefixed hex.</summary>
    public string FromAddressText { get; set; } = "0";

    /// <summary>End of a custom range (inclusive), decimal or 0x-prefixed hex.</summary>
    public string ToAddressText { get; set; } = "999";

    public string StrategyId { get; set; } = SequentialCrawlStrategy.Ascending.Id;

    public int ResponseTimeoutMs { get; set; } = (int)SlaveScanOptions.DefaultResponseTimeout.TotalMilliseconds;

    public int InterRequestDelayMs { get; set; } = (int)SlaveScanOptions.DefaultInterRequestDelay.TotalMilliseconds;

    /// <summary>Frame gap override in milliseconds; null uses max(t3.5, 20 ms).</summary>
    public double? FrameGapMs { get; set; }

    public static AddressRange? GetPresetRange(AddressRangePreset preset) => preset switch
    {
        AddressRangePreset.First1000 => new AddressRange(0, 999),
        AddressRangePreset.First10000 => new AddressRange(0, 9999),
        AddressRangePreset.Full => AddressRange.Full,
        _ => null,
    };

    public static string GetPresetName(AddressRangePreset preset) => preset switch
    {
        AddressRangePreset.First1000 => "0–999",
        AddressRangePreset.First10000 => "0–9999",
        AddressRangePreset.Full => "0–65535 (full)",
        _ => "Custom",
    };

    public void SetTableSelected(CrawlTable table, bool selected)
    {
        if (selected)
        {
            Tables.Add(table);
        }
        else
        {
            Tables.Remove(table);
        }
    }

    /// <summary>Copies the preset's bounds into the custom fields, so switching to Custom starts from them.</summary>
    public void ApplyPreset()
    {
        if (GetPresetRange(RangePreset) is AddressRange range)
        {
            FromAddressText = range.From.ToString(CultureInfo.InvariantCulture);
            ToAddressText = range.To.ToString(CultureInfo.InvariantCulture);
        }
    }

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

    public CrawlFormValidation Validate()
    {
        var errors = new Dictionary<string, string>();
        var serial = ISerialSettingsForm.Validate(this, errors);

        if (SlaveId is < SlaveIdRange.FirstId or > SlaveIdRange.LastExtendedId)
        {
            errors[nameof(SlaveId)] = $"Slave ID must be between {SlaveIdRange.FirstId} and {SlaveIdRange.LastExtendedId}.";
        }

        if (Tables.Count == 0)
        {
            errors[nameof(Tables)] = "Select at least one table.";
        }

        var range = ValidateRange(errors);

        var strategy = CrawlStrategies.Find(StrategyId);
        if (strategy is null)
        {
            errors[nameof(StrategyId)] = "Select a crawl order.";
        }

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

        CrawlOptions? options = null;
        if (errors.Count == 0)
        {
            options = new CrawlOptions
            {
                Serial = serial!,
                SlaveId = (byte)SlaveId,
                Tables = CrawlTables.All.Where(Tables.Contains).ToArray(),
                Range = range!.Value,
                Strategy = strategy!,
                ResponseTimeout = TimeSpan.FromMilliseconds(ResponseTimeoutMs),
                InterRequestDelay = TimeSpan.FromMilliseconds(InterRequestDelayMs),
                FrameGap = FrameGapMs is double frameGap ? TimeSpan.FromMilliseconds(frameGap) : null,
            };
        }

        return new CrawlFormValidation(errors, serial, range, options);
    }

    private AddressRange? ValidateRange(Dictionary<string, string> errors)
    {
        if (GetPresetRange(RangePreset) is AddressRange preset)
        {
            return preset;
        }

        if (!Hex.TryParseNumber(FromAddressText, AddressRange.MaxAddress, out var from) ||
            !Hex.TryParseNumber(ToAddressText, AddressRange.MaxAddress, out var to))
        {
            errors[nameof(AddressRange)] = $"Addresses must be 0-{AddressRange.MaxAddress} (decimal or 0x hex).";
            return null;
        }

        if (!AddressRange.TryCreate(from, to, out var range, out var error))
        {
            errors[nameof(AddressRange)] = error!;
            return null;
        }

        return range;
    }
}

/// <summary>Validation outcome of <see cref="CrawlFormModel"/>; the parts that are valid are available for previews.</summary>
public sealed record CrawlFormValidation(
    IReadOnlyDictionary<string, string> Errors,
    SerialSettings? Serial,
    AddressRange? Range,
    CrawlOptions? Options)
{
    public bool IsValid => Options is not null;

    public string? ErrorFor(string field) => Errors.GetValueOrDefault(field);
}
