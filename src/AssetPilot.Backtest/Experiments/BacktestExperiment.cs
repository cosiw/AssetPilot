using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Experiments;

public sealed record BacktestExperiment(
    ExitParameterCandidate Candidate,
    StrategyOptions StrategyOptions,
    BacktestResult? Result,
    BacktestMetrics? Metrics,
    BacktestPerformanceReport? PerformanceReport,
    BacktestEquityCurve? EquityCurve,
    string? Error,
    Guid? BacktestId = null);
