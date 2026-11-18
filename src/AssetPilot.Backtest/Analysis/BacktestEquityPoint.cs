namespace AssetPilot.Backtest.Analysis;

public sealed record BacktestEquityPoint(
    DateTime DateTime,
    decimal Cash,
    decimal PositionQuantity,
    decimal AverageEntryPrice,
    decimal MarkPrice,
    decimal PositionValue,
    decimal TotalEquity,
    decimal CumulativeReturn,
    decimal PeakEquity,
    decimal DrawdownAmount,
    decimal DrawdownRate);

public sealed record BacktestEquityCurve(
    IReadOnlyList<BacktestEquityPoint> Points,
    decimal MaxDrawdownAmount,
    decimal MaxDrawdownRate,
    DateTime? PeakAt,
    DateTime? TroughAt);
