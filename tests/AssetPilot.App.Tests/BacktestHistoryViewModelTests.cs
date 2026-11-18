using AssetPilot.App.ViewModels;
using AssetPilot.Backtest.Storage;

namespace AssetPilot.App.Tests;

public sealed class BacktestHistoryViewModelTests
{
    [Fact]
    public async Task LoadAsync_orders_history_and_limits_comparison_to_three_runs()
    {
        var newest = Summary("2026-01-04T00:00:00Z");
        var store = new InMemoryStore([
            newest,
            Summary("2026-01-03T00:00:00Z"),
            Summary("2026-01-02T00:00:00Z"),
            Summary("2026-01-01T00:00:00Z"),
        ]);
        var viewModel = new BacktestHistoryViewModel(store);

        await viewModel.LoadAsync();
        foreach (var run in viewModel.Runs) viewModel.ToggleComparisonCommand.Execute(run);

        Assert.Equal(newest.Id, viewModel.Runs[0].Id);
        Assert.Equal(3, viewModel.ComparisonRuns.Count);
    }

    private static BacktestRunSummary Summary(string executedAt) => new(Guid.NewGuid(), DateTimeOffset.Parse(executedAt), "ATR", "KODEX 200", 1m, 0.01m);

    private sealed class InMemoryStore(IReadOnlyList<BacktestRunSummary> summaries) : IBacktestRunStore
    {
        public Task SaveAsync(BacktestRun run, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BacktestRun?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<BacktestRun?>(null);
        public Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(CancellationToken cancellationToken = default) => Task.FromResult(summaries);
        public Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BacktestRunSummary>>(summaries.Where(summary => ids.Contains(summary.Id)).ToArray());
    }
}
