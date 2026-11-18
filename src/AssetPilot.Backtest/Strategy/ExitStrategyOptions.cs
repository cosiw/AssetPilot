namespace AssetPilot.Backtest.Strategy;

public sealed record ExitStrategyOptions(
    decimal TargetAtrMultiplier,
    decimal StopAtrMultiplier,
    int MaxHoldingDays)
{
    public static ExitStrategyOptions Default { get; } = new(1.5m, 1m, 5);
}
