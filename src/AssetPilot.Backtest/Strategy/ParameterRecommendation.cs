namespace AssetPilot.Backtest.Strategy;

public enum ParameterDecisionStatus { InsufficientHistory, PendingApproval, Approved, Rejected }

public sealed record ParameterRecommendation(
    ParameterDecisionStatus Status,
    DateOnly DecisionDate,
    DateOnly EffectiveFrom,
    StrategyOptions? Options,
    string Reason)
{
    public int CompletedTrades { get; init; }
    public decimal ValidationReturnRate { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
}
