using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;

namespace AssetPilot.Backtest.Storage;

public sealed record BacktestRun(
    Guid Id, DateTimeOffset ExecutedAt, string StrategyKey, string StrategyName, string StrategyOptionsJson,
    string Symbol, decimal StartingCash, decimal EndingCash, decimal TotalProfit, decimal ReturnRate,
    IReadOnlyList<BacktestExecution> Executions,
    IReadOnlyList<BacktestDiagnostic>? Diagnostics = null,
    IReadOnlyList<BacktestPositionSnapshot>? PositionSnapshots = null,
    IReadOnlyList<BacktestPositionCycle>? CompletedPositionCycles = null,
    DateOnly? DataStartDate = null,
    DateOnly? DataEndDate = null,
    BacktestAnalysisOptions? AnalysisOptions = null,
    IReadOnlyList<BacktestExcursion>? ExcursionResults = null,
    BacktestPerformanceReport? PerformanceReport = null,
    BacktestEquityCurve? EquityCurve = null,
    Guid? ExperimentId = null)
{
    public IReadOnlyList<BacktestDiagnostic> DiagnosticsOrEmpty => Diagnostics ?? [];
    public IReadOnlyList<BacktestPositionSnapshot> PositionSnapshotsOrEmpty => PositionSnapshots ?? [];
    public IReadOnlyList<BacktestPositionCycle> CompletedPositionCyclesOrEmpty => CompletedPositionCycles ?? [];
    public IReadOnlyList<BacktestExcursion> Excursions => ExcursionResults ?? [];
}
