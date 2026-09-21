using ModbusTools.Core.Scanning;

namespace ModbusTools.Core.BaudSweeping;

/// <summary>
/// Decides which rates a sweep tries, in which order, and how many requests each one gets. A strategy is an
/// immutable description that can be shown in the UI and exports; the decisions for a run, and the state they need,
/// live in the <see cref="IBaudSweepPlanner"/> it creates.
/// </summary>
/// <remarks>
/// Strategies only choose the next rate. Opening the port, sending requests, recording results, the window analysis,
/// the chart and the exports are shared by all of them, so a new search method is a new strategy and nothing else.
/// </remarks>
public interface IBaudSweepStrategy
{
    /// <summary>Stable identifier used in exports, e.g. "center-out".</summary>
    string Id { get; }

    /// <summary>Short name for the UI.</summary>
    string Name { get; }

    /// <summary>One-paragraph explanation for the UI.</summary>
    string Description { get; }

    /// <summary>What <see cref="BaudSweepOptions.RequestsPerRate"/> means for this strategy, for the form's hint.</summary>
    string RequestsNote { get; }

    /// <summary>
    /// What <see cref="BaudSweepOptions.VerifyEdgePasses"/> means for this strategy, for the form's hint; null when the
    /// strategy does not verify edges and the setting does not apply.
    /// </summary>
    string? VerifyEdgesNote => null;

    /// <summary>
    /// What the dead band saves for this strategy, for the form's hint, when <see cref="BaudSweepOptions.UseDeadBand"/>
    /// can turn it off; null when the strategy cannot do without it or does not use it.
    /// </summary>
    string? DeadBandNote => null;

    /// <summary>Fewest and most requests the strategy can send.</summary>
    RequestEstimate EstimateRequests(BaudSweepOptions options);

    /// <param name="options">The sweep's settings.</param>
    /// <param name="results">The run's results as they are recorded, for planners that plan from the whole picture.</param>
    IBaudSweepPlanner CreatePlanner(BaudSweepOptions options, BaudSweepResult results);
}

/// <summary>Plans the rates of one sweep; created per run by <see cref="IBaudSweepStrategy.CreatePlanner"/>.</summary>
public interface IBaudSweepPlanner
{
    /// <summary>
    /// The next rate to probe, or null when the sweep is done. After a step is returned,
    /// <see cref="OnCompleted"/> is called with its outcome before this method is called again.
    /// </summary>
    BaudSweepStep? Next();

    /// <summary>Reports the outcome of the step last returned by <see cref="Next"/>.</summary>
    void OnCompleted(BaudSweepStepResult result);

    /// <summary>
    /// Requests the planner now expects to send in total, refined while it learns where the edges are; null while it
    /// cannot tell, which leaves the progress bar on the up-front estimate.
    /// </summary>
    int? ExpectedRequests => null;
}

/// <param name="BaudRate">The rate to open the port at.</param>
/// <param name="Requests">Requests to send at that rate before moving on.</param>
public readonly record struct BaudSweepStep(int BaudRate, int Requests);

/// <param name="Step">The step that was carried out.</param>
/// <param name="Answered">Requests answered during this visit.</param>
/// <param name="Rate">Totals for that rate over the whole sweep, including this visit.</param>
public sealed record BaudSweepStepResult(BaudSweepStep Step, int Answered, BaudRateResult Rate)
{
    /// <summary>True when the rate has never answered, in this visit or an earlier one.</summary>
    public bool IsDead => Rate.Answered == 0;
}
