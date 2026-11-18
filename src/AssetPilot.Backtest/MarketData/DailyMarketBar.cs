namespace AssetPilot.Backtest.MarketData;

public sealed record DailyMarketBar(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
