using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModbusTools.Core.BaudSweeping;

namespace ModbusTools.Core.Export;

/// <summary>
/// Exports a <see cref="BaudSweepReport"/> as CSV (one row per rate tried) or as JSON with the measured window, so
/// runs can be compared later: the same device warm and cold, or several units of one model.
/// </summary>
public static class BaudSweepReportExporter
{
    public const string SweepFormat = "modbus-tools/baud-sweep";
    public const int SweepFormatVersion = 1;

    // Exports are standalone files, not embedded in HTML, so characters like '+' need not be escaped.
    private static readonly BaudSweepJsonContext JsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    });

    private static readonly string[] CsvColumns =
    [
        "baud_rate", "offset_percent", "requests", "answered", "bad_replies", "no_replies", "line_errors",
        "success_percent", "working", "avg_response_ms", "port_error",
    ];

    /// <summary>
    /// CSV with the sweep settings and results as leading "# name: value" comment lines, followed by a header row
    /// and one row per rate in ascending order.
    /// </summary>
    public static string ToCsv(BaudSweepReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var csv = new StringBuilder();
        foreach (var (name, value) in DescribeSettings(report))
        {
            csv.Append("# ").Append(name).Append(": ").AppendLine(value);
        }

        csv.AppendLine(string.Join(',', CsvColumns));
        foreach (var rate in report.Result.Rates)
        {
            string[] fields =
            [
                Invariant(rate.BaudRate),
                Number(rate.OffsetPercent),
                Invariant(rate.Requests),
                Invariant(rate.Answered),
                Invariant(rate.BadReplies),
                Invariant(rate.NoReplies),
                Invariant(rate.LineErrors),
                rate.Requests == 0 ? string.Empty : Number(rate.SuccessRate * 100),
                rate.Requests == 0 ? string.Empty : rate.IsWorking ? "yes" : "no",
                FormatMilliseconds(rate.AverageResponseTime),
                rate.PortError ?? string.Empty,
            ];
            csv.AppendLine(string.Join(',', fields.Select(EscapeCsv)));
        }

        return csv.ToString();
    }

    /// <summary>The measured window, the settings it was measured with, and every rate tried.</summary>
    public static string ToJson(BaudSweepReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var options = report.Options;
        var serial = options.Serial;
        var window = report.Result.GetWindow();
        var statistics = report.Result.Statistics;
        var deviceRate = window.EstimateDeviceRate(options.AdapterAccuracyPercent);

        var document = new BaudSweepDocument(
            SweepFormat,
            SweepFormatVersion,
            new SweepSourceDocument(
                Tool: "Modbus RTU Baud Rate Sweep",
                ExportedAt: FormatTimestamp(DateTimeOffset.UtcNow),
                StartedAt: FormatTimestamp(report.StartedAt),
                FinishedAt: FormatTimestamp(report.FinishedAt),
                Outcome: report.Outcome?.ToString() ?? "NotFinished",
                ErrorMessage: report.ErrorMessage,
                Port: report.PortName,
                ActiveTimeMs: RoundMilliseconds(report.ActiveTime)!.Value),
            new SweepSettingsDocument(
                options.SlaveId,
                serial.BaudRate,
                serial.DataBits,
                serial.Parity.ToString(),
                (int)serial.StopBits,
                options.Probe.Description,
                options.Strategy.Id,
                options.SpanPercent,
                options.StepPercent,
                options.DeadBandPercent,
                options.UseDeadBand,
                options.RequestsPerRate,
                options.VerifyEdgePasses,
                options.AdapterAccuracyPercent,
                RoundMilliseconds(options.ResponseTimeout)!.Value,
                RoundMilliseconds(options.InterRequestDelay)!.Value,
                RoundMilliseconds(options.FrameGapAt(serial))!.Value,
                RoundMilliseconds(options.PortSettleDelay)!.Value),
            new SweepWindowDocument(
                window.Status.ToString(),
                Round(window.Lower?.BaudRate),
                Round(window.Upper?.BaudRate),
                Round(window.Lower?.OffsetPercent),
                Round(window.Upper?.OffsetPercent),
                Round(window.CenterBaudRate),
                Round(window.CenterOffsetPercent),
                Round(window.TolerancePercent),
                Round(window.MarginDownPercent),
                Round(window.MarginUpPercent),
                Round(deviceRate?.Low),
                Round(deviceRate?.High)),
            new SweepStatisticsDocument(
                statistics.Requests,
                statistics.CountOf(ReplyStatus.Answered),
                statistics.CountOf(ReplyStatus.BadReply),
                statistics.CountOf(ReplyStatus.NoReply),
                statistics.LineErrors,
                window.TestedRates,
                window.RefusedRates,
                RoundMilliseconds(statistics.AverageResponseTime)),
            report.Result.GetEdgeChecks().Select(check => new SweepEdgeCheckDocument(
                check.Side.ToString(),
                Math.Round(check.Outward.OffsetPercent, 4),
                check.Moved,
                check.Passes.Select(shift => new SweepEdgeShiftDocument(
                    shift.Pass,
                    Math.Round(shift.ShiftPercent, 4),
                    shift.PastCheckedRates,
                    shift.Moved)).ToArray())).ToArray(),
            report.Result.Rates.Select(rate => new SweepRateDocument(
                rate.BaudRate,
                Math.Round(rate.OffsetPercent, 4),
                rate.Requests,
                rate.Answered,
                rate.BadReplies,
                rate.NoReplies,
                rate.LineErrors,
                rate.Requests == 0 ? null : Math.Round(rate.SuccessRate * 100, 2),
                rate.Requests == 0 ? null : rate.IsWorking,
                RoundMilliseconds(rate.AverageResponseTime),
                rate.PortError)).ToArray());

        return JsonSerializer.Serialize(document, JsonContext.BaudSweepDocument);
    }

    /// <summary>Sweep settings, outcome and measured window as name/value pairs.</summary>
    public static IReadOnlyList<(string Name, string Value)> DescribeSettings(BaudSweepReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var options = report.Options;
        var window = report.Result.GetWindow();
        var statistics = report.Result.Statistics;
        var deviceRate = window.EstimateDeviceRate(options.AdapterAccuracyPercent);
        var checks = report.Result.GetEdgeChecks();
        var lowerCheck = checks.FirstOrDefault(check => check.Side == BaudEdgeSide.Lower);
        var upperCheck = checks.FirstOrDefault(check => check.Side == BaudEdgeSide.Upper);

        return new List<(string Name, string Value)>
        {
            ("tool", "Modbus RTU Baud Rate Sweep"),
            ("exported_at", FormatTimestamp(DateTimeOffset.UtcNow) ?? string.Empty),
            ("started_at", FormatTimestamp(report.StartedAt) ?? string.Empty),
            ("finished_at", FormatTimestamp(report.FinishedAt) ?? string.Empty),
            ("active_time_ms", FormatMilliseconds(report.ActiveTime)),
            ("outcome", report.Outcome?.ToString() ?? "NotFinished"),
            ("error", report.ErrorMessage ?? string.Empty),
            ("port", report.PortName),
            ("serial", options.Serial.ToString()),
            ("slave_id", Invariant(options.SlaveId)),
            ("request", options.Probe.Description),
            ("strategy", options.Strategy.Id),
            ("expected_baud_rate", Invariant(options.Serial.BaudRate)),
            ("span_percent", Number(options.SpanPercent)),
            ("step_percent", Number(options.StepPercent)),
            ("dead_band_percent", Number(options.DeadBandPercent)),
            ("use_dead_band", options.UseDeadBand ? "yes" : "no"),
            ("requests_per_rate", Invariant(options.RequestsPerRate)),
            ("verify_edge_passes", Invariant(options.VerifyEdgePasses)),
            ("adapter_accuracy_percent", options.AdapterAccuracyPercent is double accuracy ? Number(accuracy) : string.Empty),
            ("timeout_ms", FormatMilliseconds(options.ResponseTimeout)),
            ("inter_request_delay_ms", FormatMilliseconds(options.InterRequestDelay)),
            ("frame_gap_ms", FormatMilliseconds(options.FrameGapAt(options.Serial))),
            ("port_settle_ms", FormatMilliseconds(options.PortSettleDelay)),
            ("requests", Invariant(statistics.Requests)),
            ("answered", Invariant(statistics.CountOf(ReplyStatus.Answered))),
            ("bad_replies", Invariant(statistics.CountOf(ReplyStatus.BadReply))),
            ("no_replies", Invariant(statistics.CountOf(ReplyStatus.NoReply))),
            ("line_errors", Invariant(statistics.LineErrors)),
            ("rates_tested", Invariant(window.TestedRates)),
            ("rates_refused", Invariant(window.RefusedRates)),
            ("window_status", window.Status.ToString()),
            ("window_low_baud", Number(window.Lower?.BaudRate)),
            ("window_high_baud", Number(window.Upper?.BaudRate)),
            ("window_low_percent", Number(window.Lower?.OffsetPercent)),
            ("window_high_percent", Number(window.Upper?.OffsetPercent)),
            ("device_baud_rate", Number(window.CenterBaudRate)),
            ("device_offset_percent", Number(window.CenterOffsetPercent)),
            ("tolerance_percent", Number(window.TolerancePercent)),
            ("margin_down_percent", Number(window.MarginDownPercent)),
            ("margin_up_percent", Number(window.MarginUpPercent)),
            ("device_baud_rate_low", Number(deviceRate?.Low)),
            ("device_baud_rate_high", Number(deviceRate?.High)),
            ("lower_edge_moved", lowerCheck is null ? string.Empty : lowerCheck.Moved ? "yes" : "no"),
            ("lower_edge_shift_percent", Number(lowerCheck?.LargestMove?.ShiftPercent)),
            ("upper_edge_moved", upperCheck is null ? string.Empty : upperCheck.Moved ? "yes" : "no"),
            ("upper_edge_shift_percent", Number(upperCheck?.LargestMove?.ShiftPercent)),
        };
    }

    private static double? Round(double? value) => value is double number ? Math.Round(number, 2) : null;

    private static string? FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string FormatMilliseconds(TimeSpan? span) =>
        span?.TotalMilliseconds.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;

    private static double? RoundMilliseconds(TimeSpan? span) =>
        span is TimeSpan value ? Math.Round(value.TotalMilliseconds, 1) : null;

    private static string Number(double? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string EscapeCsv(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}

internal sealed record BaudSweepDocument(
    string Format,
    int FormatVersion,
    SweepSourceDocument Source,
    SweepSettingsDocument Settings,
    SweepWindowDocument Window,
    SweepStatisticsDocument Statistics,
    IReadOnlyList<SweepEdgeCheckDocument> EdgeChecks,
    IReadOnlyList<SweepRateDocument> Rates);

internal sealed record SweepSourceDocument(
    string Tool,
    string? ExportedAt,
    string? StartedAt,
    string? FinishedAt,
    string Outcome,
    string? ErrorMessage,
    string Port,
    double ActiveTimeMs);

internal sealed record SweepSettingsDocument(
    int SlaveId,
    int ExpectedBaudRate,
    int DataBits,
    string Parity,
    int StopBits,
    string Request,
    string Strategy,
    double SpanPercent,
    double StepPercent,
    double DeadBandPercent,
    bool UseDeadBand,
    int RequestsPerRate,
    int VerifyEdgePasses,
    double? AdapterAccuracyPercent,
    double TimeoutMs,
    double InterRequestDelayMs,
    double FrameGapMs,
    double PortSettleMs);

internal sealed record SweepWindowDocument(
    string Status,
    double? LowBaudRate,
    double? HighBaudRate,
    double? LowOffsetPercent,
    double? HighOffsetPercent,
    double? DeviceBaudRate,
    double? DeviceOffsetPercent,
    double? TolerancePercent,
    double? MarginDownPercent,
    double? MarginUpPercent,
    double? DeviceBaudRateLow,
    double? DeviceBaudRateHigh);

internal sealed record SweepStatisticsDocument(
    int Requests,
    int Answered,
    int BadReplies,
    int NoReplies,
    int LineErrors,
    int RatesTested,
    int RatesRefused,
    double? AverageResponseTimeMs);

/// <param name="Moved">Whether any edge pass moved the edge by more than chance explains.</param>
internal sealed record SweepEdgeCheckDocument(
    string Side,
    double OutwardOffsetPercent,
    bool Moved,
    IReadOnlyList<SweepEdgeShiftDocument> Passes);

/// <param name="ShiftPercent">Positive outwards, negative inwards; a lower bound when PastCheckedRates is set.</param>
internal sealed record SweepEdgeShiftDocument(int Pass, double ShiftPercent, bool PastCheckedRates, bool Moved);

internal sealed record SweepRateDocument(
    int BaudRate,
    double OffsetPercent,
    int Requests,
    int Answered,
    int BadReplies,
    int NoReplies,
    int LineErrors,
    double? SuccessPercent,
    bool? Working,
    double? AverageResponseTimeMs,
    string? PortError);

[JsonSerializable(typeof(BaudSweepDocument))]
internal sealed partial class BaudSweepJsonContext : JsonSerializerContext;
