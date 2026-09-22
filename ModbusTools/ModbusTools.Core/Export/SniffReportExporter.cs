using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModbusTools.Core.Protocol;
using ModbusTools.Core.Sniffing;

namespace ModbusTools.Core.Export;

/// <summary>
/// Exports a <see cref="SniffReport"/> as CSV (the decoded log, one row per entry) or as a raw capture (JSON): the
/// chunks exactly as received, with their times, so the capture can be decoded again later.
/// </summary>
public static class SniffReportExporter
{
    public const string CaptureFormat = "modbus-tools/capture";
    public const int CaptureFormatVersion = 1;

    // Exports are standalone files, not embedded in HTML, so characters like '+' need not be escaped.
    private static readonly SniffReportJsonContext JsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    });

    private static readonly string[] CsvColumns =
    [
        "number", "time_ms", "kind", "slave", "function_code", "function", "summary", "outcome", "paired_with",
        "response_time_ms", "in_request_chunk", "hex",
    ];

    /// <summary>
    /// CSV with the capture settings and statistics as leading "# name: value" comment lines, followed by a header row
    /// and one row per kept log entry, oldest first. Frames are space-separated uppercase hex.
    /// </summary>
    public static string ToCsv(SniffReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var csv = new StringBuilder();
        foreach (var (name, value) in DescribeSettings(report))
        {
            csv.Append("# ").Append(name).Append(": ").AppendLine(value);
        }

        csv.AppendLine(string.Join(',', CsvColumns));
        foreach (var entry in report.Capture.Entries)
        {
            var paired = entry.Reply ?? entry.Request;
            string[] fields =
            [
                entry.Number.ToString(CultureInfo.InvariantCulture),
                FormatMilliseconds(entry.Time),
                entry.Kind.ToString(),
                entry.Address is byte address ? Invariant(address) : string.Empty,
                entry.FunctionCode is byte function ? Invariant(function) : string.Empty,
                entry.FunctionCode is byte code ? FunctionCodes.GetLabel(code) : string.Empty,
                entry.Summary ?? string.Empty,
                entry.Outcome?.ToString() ?? (entry.IsUnexpected ? "Unexpected" : string.Empty),
                paired?.Number.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                FormatMilliseconds(entry.ResponseTime),
                entry.Request is not null ? (entry.IsInRequestChunk ? "true" : "false") : string.Empty,
                Hex.Format(entry.Bytes.Span),
            ];
            csv.AppendLine(string.Join(',', fields.Select(EscapeCsv)));
        }

        return csv.ToString();
    }

    /// <summary>The received chunks and line errors with their times, and the settings needed to decode them again.</summary>
    public static string ToCaptureJson(SniffReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var options = report.Options;
        var serial = options.Serial;

        var document = new CaptureDocument(
            CaptureFormat,
            CaptureFormatVersion,
            Protocol: "rtu",
            new CaptureSerialDocument(serial.BaudRate, serial.DataBits, serial.Parity.ToString(), (int)serial.StopBits),
            RoundMilliseconds(options.FrameGap),
            RoundMilliseconds(options.ResponseTimeout),
            report.PortName,
            FormatTimestamp(report.CaptureStartedAt),
            FormatTimestamp(DateTimeOffset.UtcNow),
            report.Capture.DroppedEntries,
            report.Capture.Chunks.Select(ToDocument).ToArray());

        return JsonSerializer.Serialize(document, JsonContext.CaptureDocument);
    }

    /// <summary>Capture settings, outcome and statistics as name/value pairs.</summary>
    public static IReadOnlyList<(string Name, string Value)> DescribeSettings(SniffReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var options = report.Options;
        var serial = options.Serial;
        var statistics = report.Capture.Statistics;

        return
        [
            ("tool", "Modbus RTU Bus Sniffer"),
            ("exported_at", FormatTimestamp(DateTimeOffset.UtcNow) ?? string.Empty),
            ("capture_started_at", FormatTimestamp(report.CaptureStartedAt) ?? string.Empty),
            ("finished_at", FormatTimestamp(report.FinishedAt) ?? string.Empty),
            ("capture_time_ms", FormatMilliseconds(report.CaptureTime)),
            ("outcome", report.Outcome?.ToString() ?? "NotFinished"),
            ("error", report.ErrorMessage ?? string.Empty),
            ("port", report.PortName),
            ("serial", serial.ToString()),
            ("response_timeout_ms", FormatMilliseconds(options.ResponseTimeout)),
            ("frame_gap_ms", FormatMilliseconds(options.FrameGap)),
            ("capacity", Invariant(options.Capacity)),
            ("entries_kept", Invariant(report.Capture.Entries.Count)),
            ("entries_dropped", report.Capture.DroppedEntries.ToString(CultureInfo.InvariantCulture)),
            ("bytes", statistics.Bytes.ToString(CultureInfo.InvariantCulture)),
            ("frames", Invariant(statistics.Frames)),
            ("requests", Invariant(statistics.Requests)),
            ("broadcasts", Invariant(statistics.Broadcasts)),
            ("responses", Invariant(statistics.Responses)),
            ("garbled_runs", Invariant(statistics.GarbledRuns)),
            ("garbled_bytes", statistics.GarbledBytes.ToString(CultureInfo.InvariantCulture)),
            ("line_errors", Invariant(statistics.LineErrors)),
            ("bus_load_percent", (statistics.BusLoad(serial, report.CaptureTime) * 100).ToString("0.#", CultureInfo.InvariantCulture)),
        ];
    }

    private static ChunkDocument ToDocument(CaptureChunk chunk) => chunk.Kind == CaptureChunkKind.Data
        ? new ChunkDocument(RoundMilliseconds(chunk.Time), "data", Hex.Format(chunk.Bytes.Span))
        : new ChunkDocument(RoundMilliseconds(chunk.Time), "lineError", Hex: null);

    private static string? FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FormatMilliseconds(TimeSpan? span) =>
        span?.TotalMilliseconds.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;

    private static double RoundMilliseconds(TimeSpan span) => Math.Round(span.TotalMilliseconds, 1);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string EscapeCsv(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}

internal sealed record CaptureDocument(
    string Format,
    int FormatVersion,
    string Protocol,
    CaptureSerialDocument Serial,
    double FrameGapMs,
    double ResponseTimeoutMs,
    string Port,
    string? StartedAt,
    string? ExportedAt,
    long DroppedEntries,
    IReadOnlyList<ChunkDocument> Chunks);

internal sealed record CaptureSerialDocument(int BaudRate, int DataBits, string Parity, int StopBits);

/// <param name="TimeMs">When the chunk was received, from the start of the capture.</param>
/// <param name="Kind">"data" for received bytes, "lineError" where the port reported one.</param>
internal sealed record ChunkDocument(double TimeMs, string Kind, string? Hex);

[JsonSerializable(typeof(CaptureDocument))]
internal sealed partial class SniffReportJsonContext : JsonSerializerContext;
