namespace AssetPilot.Backtest.Storage;

public sealed record BacktestRunSummary(Guid Id, DateTimeOffset ExecutedAt, string StrategyName, string Symbol, decimal TotalProfit, decimal ReturnRate);
