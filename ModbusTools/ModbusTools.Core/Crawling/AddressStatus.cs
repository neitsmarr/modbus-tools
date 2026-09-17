namespace ModbusTools.Core.Crawling;

/// <summary>What is known about one address of a table.</summary>
public enum AddressStatus : byte
{
    /// <summary>No read has determined the address yet.</summary>
    NotChecked,

    /// <summary>Read successfully; the value is stored.</summary>
    Valid,

    /// <summary>Exception 02 Illegal Data Address.</summary>
    Invalid,

    /// <summary>Any exception other than 02.</summary>
    Exception,

    /// <summary>Nothing arrived before the timeout.</summary>
    NoResponse,

    /// <summary>A garbled reply or a reply from another slave ID.</summary>
    CommError,
}

/// <summary>What the results say about a table as a whole.</summary>
public enum TableSupport
{
    /// <summary>No address has been checked.</summary>
    NotChecked,

    /// <summary>At least one address is valid.</summary>
    Supported,

    /// <summary>The device answered, but with exceptions for every checked address.</summary>
    NoValidAddresses,

    /// <summary>Every answer was exception 01 Illegal Function.</summary>
    NotSupported,

    /// <summary>Only timeouts and communication errors.</summary>
    NoAnswer,
}

public static class AddressStatusExtensions
{
    public static string GetDisplayName(this AddressStatus status) => status switch
    {
        AddressStatus.Valid => "Valid",
        AddressStatus.Invalid => "Invalid",
        AddressStatus.Exception => "Exception",
        AddressStatus.NoResponse => "No response",
        AddressStatus.CommError => "Comm error",
        _ => "Not checked",
    };

    public static string GetDisplayName(this TableSupport support) => support switch
    {
        TableSupport.Supported => "Supported",
        TableSupport.NoValidAddresses => "No valid addresses in range",
        TableSupport.NotSupported => "Not supported (Illegal Function)",
        TableSupport.NoAnswer => "No answer",
        _ => "Not checked",
    };
}

/// <summary>Snapshot of one address.</summary>
/// <param name="Value">The register value, or 0/1 for a bit. Only meaningful when <paramref name="Status"/> is Valid.</param>
/// <param name="ExceptionCode">Set for <see cref="AddressStatus.Invalid"/> and <see cref="AddressStatus.Exception"/>.</param>
/// <param name="ResponseTime">From the estimated end of transmission to the first response byte; null without a reply.</param>
/// <param name="Detail">Diagnostic note, e.g. why a reply was garbled.</param>
public readonly record struct AddressEntry(
    CrawlTable Table,
    ushort Address,
    AddressStatus Status,
    ushort Value,
    byte? ExceptionCode,
    TimeSpan? ResponseTime,
    string? Detail);

/// <summary>A maximal run of consecutive addresses with the same status.</summary>
public readonly record struct AddressSegment(AddressRange Range, AddressStatus Status);
