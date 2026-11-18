using AssetPilot.Backtest.Execution;

namespace AssetPilot.Backtest.Analysis;

public sealed record ExitReasonPerformance(
    ExitReason? Reason,
    int TradeCount,
    decimal TotalProfit,
    decimal WinRate,
    decimal AverageReturn);

public sealed record MonthlyPerformance(
    DateOnly Month,
    int TradeCount,
    decimal TotalProfit,
    decimal WinRate,
    decimal AverageReturn);

public sealed record BacktestPerformanceReport(
    int CompletedTrades,
    decimal TotalProfit,
    decimal TotalReturn,
    decimal Cagr,
    decimal WinRate,
    decimal ProfitFactor,
    decimal AverageReturn,
    decimal AverageWin,
    decimal AverageLoss,
    int MaxConsecutiveLosses,
    IReadOnlyList<ExitReasonPerformance> ExitReasonPerformances,
    IReadOnlyList<MonthlyPerformance> MonthlyPerformances,
    IReadOnlyList<BacktestExcursion> Excursions);
