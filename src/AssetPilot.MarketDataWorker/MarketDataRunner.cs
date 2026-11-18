using AssetPilot.Backtest.MarketData;

namespace AssetPilot.MarketDataWorker;

public sealed class MarketDataRunner(IMarketDataProvider provider, CsvMarketDataStore store, TimeProvider timeProvider)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (!MarketDataSchedule.IsEligible(now)) return false;
        var end = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(9)).DateTime);
        var start = end.AddDays(-14);
        var daily = await provider.GetDailyAsync("069500", start, end, cancellationToken);
        var intraday = await provider.GetFiveMinuteAsync("069500", start, end, cancellationToken);
        await store.MergeDailyAsync("069500", daily, cancellationToken);
        await store.MergeFiveMinuteAsync("069500", intraday, cancellationToken);
        return true;
    }
}
