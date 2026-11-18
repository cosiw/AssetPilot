using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Experiments;

public sealed record ExitParameterCandidate(
    decimal TakeProfitAtrMultiplier,
    decimal StopLossAtrMultiplier,
    int MaxHoldingDays)
{
    public ExitStrategyOptions ToExitOptions() =>
        new(TakeProfitAtrMultiplier, StopLossAtrMultiplier, MaxHoldingDays);
}
