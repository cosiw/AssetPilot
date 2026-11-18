namespace AssetPilot.Backtest.MarketData;

public sealed record IntradayMarketBar(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
