namespace ModbusTools.Core.BaudSweeping;

/// <summary>The sweep strategies offered to the user, in display order.</summary>
public static class BaudSweepStrategies
{
    public static IReadOnlyList<IBaudSweepStrategy> All { get; } =
    [
        CenterOutSweepStrategy.Instance,
        RefiningSweepStrategy.Instance,
        StaircaseSweepStrategy.Instance,
        RandomSweepStrategy.Instance,
    ];

    public static IBaudSweepStrategy Default => CenterOutSweepStrategy.Instance;

    public static IBaudSweepStrategy? Find(string? id) => All.FirstOrDefault(strategy => strategy.Id == id);
}
