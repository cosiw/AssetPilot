namespace AssetPilot.Backtest.Experiments;

public sealed record BacktestExperimentReport(
    Guid ExperimentId,
    ExitParameterExperimentScope Scope,
    IReadOnlyList<BacktestExperiment> Results);
