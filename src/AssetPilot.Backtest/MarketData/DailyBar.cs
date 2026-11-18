namespace AssetPilot.Backtest.MarketData;

public sealed record DailyBar(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);
