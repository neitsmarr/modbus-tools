using System.Globalization;
using ModbusTools.Core.Crawling;

namespace ModbusTools.Crawler;

/// <summary>Display helpers shared by the crawler components.</summary>
public static class CrawlFormatting
{
    /// <summary>Address statuses in legend and filter order.</summary>
    public static IReadOnlyList<AddressStatus> StatusOrder { get; } =
    [
        AddressStatus.Valid, AddressStatus.Invalid, AddressStatus.Exception, AddressStatus.NoResponse,
        AddressStatus.CommError, AddressStatus.NotChecked,
    ];

    public static string StatusBadgeClass(AddressStatus status) => status switch
    {
        AddressStatus.Valid => "text-bg-success",
        AddressStatus.Invalid => "text-bg-secondary",
        AddressStatus.Exception => "text-bg-info",
        AddressStatus.NoResponse => "text-bg-warning",
        AddressStatus.CommError => "text-bg-danger",
        _ => "text-bg-light border",
    };

    public static string SupportBadgeClass(TableSupport support) => support switch
    {
        TableSupport.Supported => "text-bg-success",
        TableSupport.NoValidAddresses => "text-bg-secondary",
        TableSupport.NotSupported => "text-bg-dark",
        TableSupport.NoAnswer => "text-bg-warning",
        _ => "text-bg-light border",
    };

    /// <summary>Formats as "100–149", or "7" for a single address.</summary>
    public static string Range(AddressRange range) =>
        range.From == range.To ? $"{range.From}" : $"{range.From}–{range.To}";

    /// <summary>Formats as "0–35, 100–149", listing at most <paramref name="max"/> ranges.</summary>
    public static string Ranges(IReadOnlyList<AddressRange> ranges, int max = 40)
    {
        if (ranges.Count == 0)
        {
            return "none";
        }

        var shown = string.Join(", ", ranges.Take(max).Select(Range));
        return ranges.Count > max ? $"{shown} … ({Count(ranges.Count - max)} more)" : shown;
    }

    public static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Requests(RequestEstimate estimate) => estimate.IsExact
        ? Count(estimate.Minimum)
        : $"{Count(estimate.Minimum)}–{Count(estimate.Maximum)}";

    /// <summary>Formats an address as "123 (0x007B)".</summary>
    public static string Address(int address) => $"{address} (0x{address:X4})";
}
