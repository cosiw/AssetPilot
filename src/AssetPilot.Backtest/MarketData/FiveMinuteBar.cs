namespace AssetPilot.Backtest.MarketData;

public sealed record FiveMinuteBar(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);
