using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Execution;

public sealed record BacktestRequest(
    IReadOnlyList<DailyBar> DailyBars,
    IReadOnlyList<FiveMinuteBar> FiveMinuteBars,
    decimal StartingCash,
    decimal AllocationRatio,
    int MaxHoldingDays = 5,
    BacktestAnalysisOptions? AnalysisOptions = null);
