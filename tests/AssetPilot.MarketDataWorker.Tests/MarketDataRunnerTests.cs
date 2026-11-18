using AssetPilot.Backtest.MarketData;

namespace AssetPilot.MarketDataWorker.Tests;

public sealed class MarketDataRunnerTests
{
    [Fact]
    public async Task RunAsync_OnLastWeekdayOfMonth_CollectsDataWithoutParameterRecommendationDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"AssetPilot.Tests.{Guid.NewGuid():N}");
        try
        {
            var runner = new MarketDataRunner(
                new StubMarketDataProvider(),
                new CsvMarketDataStore(root),
                new FixedTimeProvider(new DateTimeOffset(2026, 11, 30, 10, 0, 0, TimeSpan.Zero)));

            var completed = await runner.RunAsync();

            Assert.True(completed);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubMarketDataProvider : IMarketDataProvider
    {
        public string Source => "test";

        public Task<IReadOnlyList<DailyMarketBar>> GetDailyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DailyMarketBar>>([new(to, 100m, 100m, 100m, 100m, 1)]);

        public Task<IReadOnlyList<IntradayMarketBar>> GetFiveMinuteAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IntradayMarketBar>>([new(to.ToDateTime(new TimeOnly(16, 0)), 100m, 100m, 100m, 100m, 1)]);
    }
}
