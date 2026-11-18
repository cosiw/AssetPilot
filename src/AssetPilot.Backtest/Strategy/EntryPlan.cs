namespace AssetPilot.Backtest.Strategy;

public sealed record EntryPlan(
    DateOnly SignalDate,
    decimal BuyLowerPrice,
    decimal BuyUpperPrice,
    decimal Atr);
