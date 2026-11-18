namespace AssetPilot.Backtest.Strategy;

public static class BaselineStrategyOptions
{
    public const string StrategyVersion = "swing-atr-baseline-v1";

    public static StrategyOptions Create(ExitStrategyOptions exitOptions) => new()
    {
        SmaPeriod = 120,
        AtrPeriod = 14,
        SmaSlopeLookbackDays = 5,
        MinimumSmaDistancePercent = 1m,
        ShortSmaPeriod = 5,
        BuyBandAtrMultiplier = 0.5m,
        TargetAtrMultiplier = exitOptions.TargetAtrMultiplier,
        StopAtrMultiplier = exitOptions.StopAtrMultiplier,
        MaxHoldingDays = exitOptions.MaxHoldingDays
    };
}
