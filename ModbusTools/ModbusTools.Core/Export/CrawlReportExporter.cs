using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModbusTools.Core.Crawling;
using ModbusTools.Core.Protocol;

namespace ModbusTools.Core.Export;

/// <summary>
/// Exports a <see cref="CrawlReport"/> as CSV (one row per checked address) or as a draft device profile (JSON) that
/// seeds a device profile: communication settings, supported tables, valid ranges and observed values.
/// </summary>
public static class CrawlReportExporter
{
    public const string DeviceProfileFormat = "modbus-tools/device-profile";
    public const int DeviceProfileFormatVersion = 1;

    // Exports are standalone files, not embedded in HTML, so characters like '+' need not be escaped.
    private static readonly CrawlReportJsonContext JsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });

    private static readonly string[] CsvColumns =
    [
        "table", "address", "address_hex", "status", "value_hex", "value_dec", "exception_code", "exception_name",
        "response_time_ms", "detail",
    ];

    /// <summary>
    /// CSV with the crawl settings and valid ranges as leading "# name: value" comment lines, followed by a header row
    /// and one row per checked address, table by table in ascending address order.
    /// </summary>
    public static string ToCsv(CrawlReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var csv = new StringBuilder();
        foreach (var (name, value) in DescribeSettings(report))
        {
            csv.Append("# ").Append(name).Append(": ").AppendLine(value);
        }

        csv.AppendLine(string.Join(',', CsvColumns));
        foreach (var table in report.Result.Tables)
        {
            foreach (var entry in table.GetCheckedEntries())
            {
                var isValid = entry.Status == AddressStatus.Valid;
                string[] fields =
                [
                    entry.Table.GetShortName(),
                    Invariant(entry.Address),
                    $"0x{entry.Address:X4}",
                    entry.Status.ToString(),
                    isValid ? entry.Table.FormatValueHex(entry.Value) : string.Empty,
                    isValid ? Invariant(entry.Value) : string.Empty,
                    entry.ExceptionCode is byte code ? code.ToString("X2", CultureInfo.InvariantCulture) : string.Empty,
                    entry.ExceptionCode is byte exceptionCode ? ModbusExceptionCodes.GetName(exceptionCode) : string.Empty,
                    FormatMilliseconds(entry.ResponseTime),
                    entry.Detail ?? string.Empty,
                ];
                csv.AppendLine(string.Join(',', fields.Select(EscapeCsv)));
            }
        }

        return csv.ToString();
    }

    /// <summary>Draft device profile: everything the crawl learned, with empty placeholders for what it cannot know.</summary>
    public static string ToDeviceProfileJson(CrawlReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var options = report.Options;
        var serial = options.Serial;
        var statistics = report.Result.Statistics;

        var document = new DeviceProfileDocument(
            DeviceProfileFormat,
            DeviceProfileFormatVersion,
            Draft: true,
            new DeviceInfoDocument(Name: null, Manufacturer: null, Model: null, Notes: null),
            new CommunicationDocument(
                options.SlaveId,
                serial.BaudRate,
                serial.DataBits,
                serial.Parity.ToString(),
                (int)serial.StopBits,
                RoundMilliseconds(statistics.MedianResponseTime),
                MaxQuantityPerRequest: null),
            report.Result.Tables.Select(ToDocument).ToArray(),
            new CrawlSourceDocument(
                Tool: "Modbus RTU Register Map Crawler",
                ExportedAt: FormatTimestamp(DateTimeOffset.UtcNow),
                StartedAt: FormatTimestamp(report.StartedAt),
                FinishedAt: FormatTimestamp(report.FinishedAt),
                Outcome: report.Outcome?.ToString() ?? "NotFinished",
                Port: report.PortName,
                Strategy: options.Strategy.Id,
                Range: options.Range.ToString(),
                TimeoutMs: options.ResponseTimeout.TotalMilliseconds,
                InterRequestDelayMs: options.InterRequestDelay.TotalMilliseconds,
                FrameGapMs: RoundMilliseconds(options.EffectiveFrameGap)!.Value,
                ActiveTimeMs: RoundMilliseconds(report.ActiveTime)!.Value,
                Requests: statistics.Requests,
                Exceptions: statistics.Exceptions,
                Timeouts: statistics.Timeouts,
                CommErrors: statistics.CommErrors,
                ExceptionCounts: statistics.GetExceptionCounts().ToDictionary(
                    pair => pair.Code.ToString("X2", CultureInfo.InvariantCulture), pair => pair.Count)));

        return JsonSerializer.Serialize(document, JsonContext.DeviceProfileDocument);
    }

    /// <summary>Crawl settings, outcome, statistics and valid ranges as name/value pairs.</summary>
    public static IReadOnlyList<(string Name, string Value)> DescribeSettings(CrawlReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var options = report.Options;
        var serial = options.Serial;
        var statistics = report.Result.Statistics;

        var settings = new List<(string Name, string Value)>
        {
            ("tool", "Modbus RTU Register Map Crawler"),
            ("exported_at", FormatTimestamp(DateTimeOffset.UtcNow) ?? string.Empty),
            ("started_at", FormatTimestamp(report.StartedAt) ?? string.Empty),
            ("finished_at", FormatTimestamp(report.FinishedAt) ?? string.Empty),
            ("active_time_ms", FormatMilliseconds(report.ActiveTime)),
            ("outcome", report.Outcome?.ToString() ?? "NotFinished"),
            ("error", report.ErrorMessage ?? string.Empty),
            ("port", report.PortName),
            ("serial", serial.ToString()),
            ("slave_id", Invariant(options.SlaveId)),
            ("tables", string.Join(' ', options.Tables.Select(table => table.GetShortName()))),
            ("range", options.Range.ToString()),
            ("strategy", options.Strategy.Id),
            ("timeout_ms", FormatMilliseconds(options.ResponseTimeout)),
            ("inter_request_delay_ms", FormatMilliseconds(options.InterRequestDelay)),
            ("frame_gap_ms", FormatMilliseconds(options.EffectiveFrameGap)),
            ("max_flush_ms", FormatMilliseconds(options.MaxFlushDuration)),
            ("requests", Invariant(statistics.Requests)),
            ("exceptions", Invariant(statistics.Exceptions)),
            ("timeouts", Invariant(statistics.Timeouts)),
            ("comm_errors", Invariant(statistics.CommErrors)),
            ("typical_response_ms", FormatMilliseconds(statistics.MedianResponseTime)),
        };

        foreach (var table in report.Result.Tables)
        {
            settings.Add(($"valid_{table.Table.GetShortName()}", FormatRanges(table.GetValidRanges())));
        }

        return settings;
    }

    private static TableDocument ToDocument(CrawlTableResult table)
    {
        var points = table.GetCheckedEntries()
            .Where(entry => entry.Status == AddressStatus.Valid)
            .Select(entry => new PointDocument(entry.Address, Name: null, entry.Value, entry.Table.FormatValueHex(entry.Value)))
            .ToArray();

        return new TableDocument(
            JsonNamingPolicy.CamelCase.ConvertName(table.Table.ToString()),
            (int)table.Table.GetFunctionCode(),
            table.Support.ToString(),
            table.Range.ToString(),
            table.CheckedCount,
            table.GetValidRanges().Select(range => range.ToString()).ToArray(),
            points);
    }

    private static string FormatRanges(IReadOnlyList<AddressRange> ranges) => string.Join(' ', ranges);

    private static string? FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FormatMilliseconds(TimeSpan? span) =>
        span?.TotalMilliseconds.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;

    private static double? RoundMilliseconds(TimeSpan? span) =>
        span is TimeSpan value ? Math.Round(value.TotalMilliseconds, 1) : null;

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string EscapeCsv(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}

internal sealed record DeviceProfileDocument(
    string Format,
    int FormatVersion,
    bool Draft,
    DeviceInfoDocument Device,
    CommunicationDocument Communication,
    IReadOnlyList<TableDocument> Tables,
    CrawlSourceDocument Source);

internal sealed record DeviceInfoDocument(string? Name, string? Manufacturer, string? Model, string? Notes);

internal sealed record CommunicationDocument(
    int SlaveId,
    int BaudRate,
    int DataBits,
    string Parity,
    int StopBits,
    double? TypicalResponseTimeMs,
    int? MaxQuantityPerRequest);

internal sealed record TableDocument(
    string Table,
    int FunctionCode,
    string Support,
    string CheckedRange,
    int CheckedAddresses,
    IReadOnlyList<string> ValidRanges,
    IReadOnlyList<PointDocument> Points);

internal sealed record PointDocument(int Address, string? Name, int Value, string ValueHex);

internal sealed record CrawlSourceDocument(
    string Tool,
    string? ExportedAt,
    string? StartedAt,
    string? FinishedAt,
    string Outcome,
    string Port,
    string Strategy,
    string Range,
    double TimeoutMs,
    double InterRequestDelayMs,
    double FrameGapMs,
    double ActiveTimeMs,
    int Requests,
    int Exceptions,
    int Timeouts,
    int CommErrors,
    IReadOnlyDictionary<string, int> ExceptionCounts);

[JsonSerializable(typeof(DeviceProfileDocument))]
internal sealed partial class CrawlReportJsonContext : JsonSerializerContext;
