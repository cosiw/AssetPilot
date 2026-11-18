namespace AssetPilot.Backtest.Strategy;

public sealed record StrategyOptions
{
    public int SmaPeriod { get; init; } = 120;

    public int AtrPeriod { get; init; } = 14;

    public int SmaSlopeLookbackDays { get; init; } = 5;

    public decimal MinimumSmaDistancePercent { get; init; } = 1m;

    public int ShortSmaPeriod { get; init; } = 20;

    public decimal BuyBandAtrMultiplier { get; init; } = 0.5m;

    public decimal TargetAtrMultiplier { get; init; } = 1.5m;

    public decimal StopAtrMultiplier { get; init; } = 1.0m;

    public int MaxHoldingDays { get; init; } = 5;
}
