using AssetPilot.Backtest.Analysis;

namespace AssetPilot.Backtest.Execution;

public sealed record BacktestResult(
    IReadOnlyList<BacktestExecution> Executions,
    IReadOnlyList<BacktestDiagnostic> Diagnostics,
    IReadOnlyList<BacktestPositionSnapshot>? Snapshots = null,
    IReadOnlyList<BacktestPositionCycle>? PositionCycles = null,
    IReadOnlyList<BacktestExcursion>? ExcursionResults = null,
    BacktestPerformanceReport? PerformanceReport = null,
    BacktestEquityCurve? EquityCurve = null)
{
    public IReadOnlyList<BacktestPositionSnapshot> PositionSnapshots => Snapshots ?? [];
    public IReadOnlyList<BacktestPositionCycle> CompletedPositionCycles => PositionCycles ?? [];
    public IReadOnlyList<BacktestExcursion> Excursions => ExcursionResults ?? [];
}
