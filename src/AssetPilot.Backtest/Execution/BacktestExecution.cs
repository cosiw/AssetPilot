namespace AssetPilot.Backtest.Execution;

public enum ExecutionSide { Buy, Sell }
public enum ExitReason { Target, Stop, Time }

public sealed record BacktestExecution(
    Guid ExecutionId,
    int Sequence,
    DateTime ExecutedAt,
    ExecutionSide Side,
    decimal Price,
    decimal Quantity,
    decimal GrossAmount,
    decimal Fee,
    decimal Tax,
    decimal? RealizedProfit = null,
    ExitReason? Reason = null)
{
    public BacktestExecution(
        DateTime executedAt,
        ExecutionSide side,
        decimal price,
        decimal quantity,
        decimal? realizedProfit = null,
        ExitReason? reason = null)
        : this(Guid.Empty, 0, executedAt, side, price, quantity, price * quantity, 0m, 0m, realizedProfit, reason)
    {
    }
}
