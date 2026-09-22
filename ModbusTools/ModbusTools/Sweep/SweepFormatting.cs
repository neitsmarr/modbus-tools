using System.Globalization;
using ModbusTools.Core.BaudSweeping;

namespace ModbusTools.Sweep;

/// <summary>Display helpers shared by the sweep components.</summary>
public static class SweepFormatting
{
    /// <summary>Reply statuses in legend, stacking and table order.</summary>
    public static IReadOnlyList<ReplyStatus> StatusOrder { get; } =
        [ReplyStatus.Answered, ReplyStatus.BadReply, ReplyStatus.NoReply];

    public static string StatusBadgeClass(ReplyStatus status) => status switch
    {
        ReplyStatus.Answered => "text-bg-success",
        ReplyStatus.BadReply => "text-bg-danger",
        _ => "text-bg-warning",
    };

    /// <summary>CSS class for a status, matching the fills in the chart's stylesheet.</summary>
    public static string ColorClass(ReplyStatus status) => $"reply-{status.ToString().ToLowerInvariant()}";

    /// <summary>Formats a baud rate as a whole number; null becomes a dash.</summary>
    public static string Baud(double? baudRate) =>
        baudRate is double rate ? rate.ToString("N0", CultureInfo.CurrentCulture) : "–";

    public static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Formats a percentage with its sign, e.g. "+1.20 %"; zero keeps no sign, null becomes a dash.</summary>
    public static string Signed(double? percent, int decimals = 2)
    {
        if (percent is not double value)
        {
            return "–";
        }

        var digits = new string('0', decimals);
        return value.ToString($"+0.{digits};-0.{digits};0.{digits}", CultureInfo.CurrentCulture) + " %";
    }

    /// <summary>Formats a percentage without a sign, e.g. "4.20 %"; null becomes a dash.</summary>
    public static string Percent(double? percent, int decimals = 2) => percent is double value
        ? value.ToString($"0.{new string('0', decimals)}", CultureInfo.CurrentCulture) + " %"
        : "–";

    public static string SuccessPercent(BaudRateResult rate) =>
        rate.Requests == 0 ? "–" : (rate.SuccessRate * 100).ToString("0", CultureInfo.CurrentCulture) + " %";

    /// <summary>What the results say and what to do about it; empty when the sweep is going fine.</summary>
    public static IReadOnlyList<string> GetHints(BaudSweepResult result, BaudSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);

        var window = result.GetWindow();
        var hints = new List<string>();

        switch (window.Status)
        {
            case BaudWindowStatus.NoWorkingRate when window.AnythingAnswered:
                hints.Add(
                    "Some requests were answered, but no rate answered at least half of them. The link is marginal " +
                    "everywhere it was tried: check the wiring and termination, or raise the timeout.");
                break;
            case BaudWindowStatus.NoWorkingRate:
                hints.Add(
                    "Nothing answered. Check the slave ID, the frame format and the wiring - or the expected rate " +
                    "itself: " + options.Strategy.NothingAnsweredNote);
                break;
            case BaudWindowStatus.Unbounded when window.TestedRates >= options.Grid.Count:
                hints.Add(
                    "Every rate in the span answered, which a real UART link cannot do: it tolerates a few percent " +
                    "of mismatch, not ten. The port is almost certainly ignoring the baud rate, as happens with " +
                    "devices that emulate a serial port over USB rather than driving a real UART.");
                break;
            case BaudWindowStatus.Unbounded:
                hints.Add("No edge was reached on either side. Widen the span so the sweep can get past the window.");
                break;
            case BaudWindowStatus.PartlyBounded:
                hints.Add($"Only the {(window.Lower is null ? "upper" : "lower")} edge was found. Widen the span to reach the other one.");
                break;
        }

        if (window.RefusedRates > 0)
        {
            hints.Add(
                $"{Count(window.RefusedRates)} rate(s) could not be opened. This adapter or its driver only produces " +
                "certain rates, so the window is only as sharp as the rates it did accept.");
        }

        if (result.Statistics.LineErrors > 0 && window.Status == BaudWindowStatus.Bounded)
        {
            hints.Add(
                $"{Count(result.Statistics.LineErrors)} framing or parity error(s) were reported. That is normal " +
                "outside the window: it is what a reply looks like when the rate no longer matches.");
        }

        return hints;
    }
}
