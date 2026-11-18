namespace AssetPilot.Backtest.Execution;

public sealed record BacktestPositionCycle(
    Guid CycleId,
    Guid OpeningExecutionId,
    Guid ClosingExecutionId,
    DateTime OpenedAt,
    DateTime ClosedAt,
    decimal BoughtQuantity,
    decimal SoldQuantity,
    decimal AverageEntryPrice,
    decimal AverageExitPrice,
    decimal RealizedProfit,
    decimal ReturnRate,
    ExitReason? ClosingReason);
