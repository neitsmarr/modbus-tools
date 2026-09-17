using System.Globalization;
using ModbusTools.Core.Protocol;

namespace ModbusTools.Scanner;

/// <summary>Display helpers shared by the scanner components.</summary>
public static class ScanFormatting
{
    public static string StatusBadgeClass(ProbeStatus status) => status switch
    {
        ProbeStatus.Ok => "text-bg-success",
        ProbeStatus.Exception => "text-bg-info",
        ProbeStatus.WrongId => "text-bg-warning",
        ProbeStatus.Garbled => "text-bg-danger",
        _ => "text-bg-secondary",
    };

    /// <summary>Formats a duration as "850 ms", "12.4 s", "3 min 05 s" or "4 h 20 min".</summary>
    public static string Duration(TimeSpan span) => span.TotalSeconds switch
    {
        < 1 => $"{span.TotalMilliseconds.ToString("0", CultureInfo.CurrentCulture)} ms",
        < 60 => $"{span.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)} s",
        < 3600 => $"{(int)span.TotalMinutes} min {span.Seconds:00} s",
        _ => $"{(int)span.TotalHours} h {span.Minutes:00} min",
    };

    public static string Milliseconds(TimeSpan? span) =>
        span?.TotalMilliseconds.ToString("0.0", CultureInfo.CurrentCulture) ?? string.Empty;
}
