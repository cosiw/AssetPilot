namespace AssetPilot.Backtest.MarketData;

public interface IMarketDataProvider
{
    string Source { get; }

    Task<IReadOnlyList<DailyMarketBar>> GetDailyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntradayMarketBar>> GetFiveMinuteAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
