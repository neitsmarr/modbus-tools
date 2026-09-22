using ModbusTools.Components.Serial;
using ModbusTools.Core.Serial;
using ModbusTools.Core.Sniffing;
using ModbusTools.Scanner;

namespace ModbusTools.Sniffer;

/// <summary>Editable state of the capture configuration form; <see cref="Validate"/> turns it into capture options.</summary>
public sealed class SniffFormModel : ISerialSettingsForm
{
    public int BaudRate { get; set; } = 19200;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.Even;

    public StopBits StopBits { get; set; } = StopBits.One;

    public int ResponseTimeoutMs { get; set; } = (int)SniffOptions.DefaultResponseTimeout.TotalMilliseconds;

    public int Capacity { get; set; } = SniffOptions.DefaultCapacity;

    /// <summary>Prefills settings passed from another tool. Missing or invalid values are ignored.</summary>
    public void ApplyQuery(int? baudRate, int? dataBits, string? parity, int? stopBits)
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
    }

    public SniffFormValidation Validate()
    {
        var errors = new Dictionary<string, string>();
        var serial = ISerialSettingsForm.Validate(this, errors);

        if (ResponseTimeoutMs is < 1 or > ScanFormModel.MaxResponseTimeoutMs)
        {
            errors[nameof(ResponseTimeoutMs)] = $"Timeout must be between 1 and {ScanFormModel.MaxResponseTimeoutMs} ms.";
        }

        if (Capacity is < 1 or > SniffOptions.MaxCapacity)
        {
            errors[nameof(Capacity)] = $"Log size must be between 1 and {SniffOptions.MaxCapacity:N0} entries.";
        }

        SniffOptions? options = null;
        if (errors.Count == 0)
        {
            options = new SniffOptions
            {
                Serial = serial!,
                ResponseTimeout = TimeSpan.FromMilliseconds(ResponseTimeoutMs),
                Capacity = Capacity,
            };
        }

        return new SniffFormValidation(errors, serial, options);
    }
}

/// <summary>Validation outcome of <see cref="SniffFormModel"/>.</summary>
public sealed record SniffFormValidation(
    IReadOnlyDictionary<string, string> Errors,
    SerialSettings? Serial,
    SniffOptions? Options)
{
    public bool IsValid => Options is not null;

    public string? ErrorFor(string field) => Errors.GetValueOrDefault(field);
}
