using System.Globalization;
using ModbusTools.Core.Sniffing;
using ModbusTools.Scanner;

namespace ModbusTools.Sniffer;

/// <summary>Display helpers shared by the sniffer components.</summary>
public static class SniffFormatting
{
    public static string KindName(SniffEntryKind kind) => kind switch
    {
        SniffEntryKind.LineError => "Line error",
        _ => kind.ToString(),
    };

    public static string KindBadgeClass(SniffEntryKind kind) => kind switch
    {
        SniffEntryKind.Request => "text-bg-primary",
        SniffEntryKind.Broadcast => "text-bg-secondary",
        SniffEntryKind.Response => "text-bg-success",
        SniffEntryKind.Exception => "text-bg-info",
        SniffEntryKind.Garbled => "text-bg-danger",
        _ => "text-bg-dark",
    };

    public static string OutcomeText(RequestOutcome outcome) => outcome switch
    {
        RequestOutcome.Waiting => "waiting…",
        RequestOutcome.Answered => "answered",
        RequestOutcome.Exception => "exception",
        RequestOutcome.NoResponse => "no response",
        RequestOutcome.GarbledReply => "garbled reply",
        _ => "capture ended",
    };

    public static string OutcomeClass(RequestOutcome outcome) => outcome switch
    {
        RequestOutcome.Answered => "text-success",
        RequestOutcome.Exception => "text-info-emphasis",
        RequestOutcome.NoResponse => "text-warning-emphasis",
        RequestOutcome.GarbledReply => "text-danger",
        _ => "text-body-secondary",
    };

    /// <summary>Formats a capture time as seconds with milliseconds, e.g. "12.345 s".</summary>
    public static string Time(TimeSpan time) => time.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture) + " s";

    /// <summary>
    /// Response time of a paired response, e.g. "12.3 ms", or "same chunk" when it arrived together with the end of
    /// its request and cannot be timed.
    /// </summary>
    public static string ResponseTime(SniffEntry response) =>
        response.ResponseTime is TimeSpan time ? $"{ScanFormatting.Milliseconds(time)} ms" : "same chunk";

    public static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Percent(double fraction) => (fraction * 100).ToString("0.0", CultureInfo.CurrentCulture) + " %";
}
