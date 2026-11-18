using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Tests;

public sealed class CsvMarketDataStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MergeFiveMinuteAsync_replaces_same_timestamp_without_duplicate()
    {
        var store = new CsvMarketDataStore(_root);
        var timestamp = new DateTime(2026, 1, 2, 9, 0, 0);
        await store.MergeFiveMinuteAsync("069500", [new IntradayMarketBar(timestamp, 100m, 110m, 99m, 105m, 1)]);
        await store.MergeFiveMinuteAsync("069500", [new IntradayMarketBar(timestamp, 101m, 111m, 100m, 106m, 2)]);

        var path = Path.Combine(_root, "market-data", "069500", "5m", "2026.csv");
        var bars = new CsvMarketDataLoader().LoadFiveMinute(path);

        Assert.Equal(new FiveMinuteBar(timestamp, 101m, 111m, 100m, 106m), Assert.Single(bars));
    }

    [Fact]
    public async Task MergeDailyAsync_rejects_invalid_ohlc_without_changing_existing_file()
    {
        var store = new CsvMarketDataStore(_root);
        await store.MergeDailyAsync("069500", [new DailyMarketBar(new DateOnly(2026, 1, 2), 100m, 110m, 90m, 105m, 1)]);

        await Assert.ThrowsAsync<ArgumentException>(() => store.MergeDailyAsync("069500", [new DailyMarketBar(new DateOnly(2026, 1, 3), 100m, 99m, 90m, 105m, 1)]));

        var path = Path.Combine(_root, "market-data", "069500", "daily.csv");
        Assert.Single(new CsvMarketDataLoader().LoadDaily(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
