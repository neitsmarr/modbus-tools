using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModbusTools.Core.Protocol;
using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.Export;

/// <summary>Exports a <see cref="ScanReport"/> as CSV (one row per ID) or JSON (full per-attempt detail).</summary>
public static class ScanReportExporter
{
    // Exports are standalone files, not embedded in HTML, so characters like '+' need not be escaped.
    private static readonly ScanReportJsonContext JsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });

    private static readonly string[] CsvColumns =
    [
        "id", "status", "garbled_reason", "exception_code", "exception_name", "reply_address", "response_time_ms",
        "raw_response_time_ms", "attempts", "inconsistent", "tx_hex", "rx_hex", "late_bytes_hex", "notes",
    ];

    /// <summary>
    /// CSV with the scan settings as leading "# name: value" comment lines, followed by a header row and one row per
    /// scanned ID. Frames are space-separated uppercase hex.
    /// </summary>
    public static string ToCsv(ScanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var csv = new StringBuilder();
        foreach (var (name, value) in DescribeSettings(report))
        {
            csv.Append("# ").Append(name).Append(": ").AppendLine(value);
        }

        csv.AppendLine(string.Join(',', CsvColumns));
        foreach (var result in report.Results)
        {
            var best = result.BestAttempt;
            var classification = best.Classification;
            string[] fields =
            [
                Invariant(result.SlaveId),
                result.Status.GetDisplayName(),
                classification.GarbledReason?.ToString() ?? string.Empty,
                classification.ExceptionCode is byte code ? code.ToString("X2", CultureInfo.InvariantCulture) : string.Empty,
                classification.ExceptionCode is byte exceptionCode ? ModbusExceptionCodes.GetName(exceptionCode) : string.Empty,
                classification.ReplyAddress is byte reply ? Invariant(reply) : string.Empty,
                FormatMilliseconds(best.ResponseTime),
                FormatMilliseconds(best.RawResponseTime),
                Invariant(result.Attempts.Count),
                result.IsInconsistent ? "true" : "false",
                Hex.Format(best.Request.Span),
                Hex.Format(best.Response.Span),
                Hex.Format(result.LateBytes.Span),
                string.Join(" ", result.Notes),
            ];
            csv.AppendLine(string.Join(',', fields.Select(EscapeCsv)));
        }

        return csv.ToString();
    }

    public static string ToJson(ScanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var document = new ScanReportDocument(
            DescribeSettings(report).ToDictionary(pair => pair.Name, pair => pair.Value),
            report.Results.Select(ToDocument).ToArray());
        return JsonSerializer.Serialize(document, JsonContext.ScanReportDocument);
    }

    /// <summary>Scan settings and timestamps as name/value pairs, shared by both formats.</summary>
    public static IReadOnlyList<(string Name, string Value)> DescribeSettings(ScanReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var options = report.Options;
        var serial = options.Serial;
        var ids = options.SlaveIds;

        return
        [
            ("tool", "Modbus RTU Slave ID Scanner"),
            ("exported_at", FormatTimestamp(DateTimeOffset.UtcNow)),
            ("started_at", FormatTimestamp(report.StartedAt)),
            ("finished_at", FormatTimestamp(report.FinishedAt)),
            ("active_time_ms", FormatMilliseconds(report.ActiveTime)),
            ("outcome", report.Outcome?.ToString() ?? "NotFinished"),
            ("error", report.ErrorMessage ?? string.Empty),
            ("port", report.PortName),
            ("serial", serial.ToString()),
            ("baud_rate", Invariant(serial.BaudRate)),
            ("data_bits", Invariant(serial.DataBits)),
            ("parity", serial.Parity.ToString()),
            ("stop_bits", Invariant((int)serial.StopBits)),
            ("ids", FormatIds(ids)),
            ("id_count", Invariant(ids.Count)),
            ("probe", options.Probe.Description),
            ("timeout_ms", FormatMilliseconds(options.TimeoutStrategy.GetResponseTimeout(ids[0], 1))),
            ("retries", Invariant(options.Retries)),
            ("inter_request_delay_ms", FormatMilliseconds(options.InterRequestDelay)),
            ("frame_gap_ms", FormatMilliseconds(options.EffectiveFrameGap)),
            ("max_flush_ms", FormatMilliseconds(options.MaxFlushDuration)),
            ("stop_at_first_found", options.StopAtFirstFound ? "true" : "false"),
            ("responded", Invariant(report.Results.Count(r => r.IsFound))),
            ("scanned", Invariant(report.Results.Count)),
        ];
    }

    private static SlaveResultDocument ToDocument(SlaveProbeResult result) => new(
        result.SlaveId,
        result.Status.ToString(),
        result.IsFound,
        result.IsInconsistent,
        result.IsProblematic,
        Math.Round(result.Duration.TotalMilliseconds, 1),
        Hex.Format(result.LateBytes.Span),
        result.Notes,
        result.Attempts.Select(ToDocument).ToArray());

    private static AttemptDocument ToDocument(ProbeAttempt attempt)
    {
        var classification = attempt.Classification;
        return new AttemptDocument(
            attempt.Number,
            FormatTimestamp(attempt.StartedAt),
            attempt.Status.ToString(),
            classification.GarbledReason?.ToString(),
            classification.ExceptionCode,
            classification.ExceptionCode is byte code ? ModbusExceptionCodes.GetName(code) : null,
            classification.ReplyAddress,
            classification.Detail,
            attempt.ResponseTimeout.TotalMilliseconds,
            attempt.ResponseTime is TimeSpan time ? Math.Round(time.TotalMilliseconds, 1) : null,
            attempt.RawResponseTime is TimeSpan raw ? Math.Round(raw.TotalMilliseconds, 1) : null,
            Hex.Format(attempt.Request.Span),
            Hex.Format(attempt.Response.Span),
            Hex.Format(attempt.LateBytes.Span),
            Hex.Format(attempt.StaleBytes.Span));
    }

    private static string FormatIds(IReadOnlyList<byte> ids)
    {
        var contiguous = ids.Skip(1).Select((id, i) => id == ids[i] + 1).All(next => next);
        return contiguous ? $"{ids[0]}-{ids[^1]}" : string.Join(' ', ids);
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatMilliseconds(TimeSpan? span) =>
        span?.TotalMilliseconds.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string EscapeCsv(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}

internal sealed record ScanReportDocument(IReadOnlyDictionary<string, string> Settings, IReadOnlyList<SlaveResultDocument> Results);

internal sealed record SlaveResultDocument(
    int Id,
    string Status,
    bool Found,
    bool Inconsistent,
    bool Problematic,
    double DurationMs,
    string LateBytesHex,
    IReadOnlyList<string> Notes,
    IReadOnlyList<AttemptDocument> Attempts);

internal sealed record AttemptDocument(
    int Number,
    string StartedAt,
    string Status,
    string? GarbledReason,
    int? ExceptionCode,
    string? ExceptionName,
    int? ReplyAddress,
    string? Detail,
    double TimeoutMs,
    double? ResponseTimeMs,
    double? RawResponseTimeMs,
    string TxHex,
    string RxHex,
    string LateBytesHex,
    string StaleBytesHex);

[JsonSerializable(typeof(ScanReportDocument))]
internal sealed partial class ScanReportJsonContext : JsonSerializerContext;
