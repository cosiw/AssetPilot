namespace AssetPilot.Backtest.Analysis;

public sealed record BacktestExcursion(
    Guid CycleId,
    decimal HoldingMfeAmount,
    decimal HoldingMfeRate,
    decimal HoldingMaeAmount,
    decimal HoldingMaeRate,
    decimal FixedMfeAmount,
    decimal FixedMfeRate,
    decimal FixedMaeAmount,
    decimal FixedMaeRate,
    decimal? PostExitMfeRate,
    decimal? PostExitMaeRate,
    int FixedObservationTradingDays);
