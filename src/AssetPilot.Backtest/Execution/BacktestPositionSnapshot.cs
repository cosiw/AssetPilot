namespace AssetPilot.Backtest.Execution;

public sealed record BacktestPositionSnapshot(
    Guid ExecutionId,
    int ExecutionSequence,
    DateTime RecordedAt,
    decimal Cash,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal PositionCost,
    decimal RealizedProfitTotal);
