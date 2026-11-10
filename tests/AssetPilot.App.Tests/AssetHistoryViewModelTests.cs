using AssetPilot.App.ViewModels;
using AssetPilot.Core.Snapshots;

namespace AssetPilot.App.Tests;

public sealed class AssetHistoryViewModelTests
{
    [Fact]
    public async Task LoadAsync_selects_latest_snapshot_and_filters_the_week_range()
    {
        var accountId = Guid.NewGuid();
        var viewModel = new AssetHistoryViewModel(
            new FakeSnapshotReader(
            [
                Summary(new DateOnly(2026, 10, 1)),
                Summary(new DateOnly(2026, 11, 3)),
                Summary(new DateOnly(2026, 11, 9))
            ]));

        await viewModel.LoadAsync(accountId);

        Assert.True(viewModel.HasHistory);
        Assert.Equal(new DateOnly(2026, 11, 9), viewModel.SelectedSnapshotDate);
        Assert.Equal([new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9)], viewModel.AvailableSnapshotDates);

        viewModel.SelectedRange = AssetHistoryRange.All;

        Assert.Equal(3, viewModel.Summaries.Count);
        Assert.Equal(new DateOnly(2026, 11, 9), viewModel.SelectedSnapshotDate);
    }

    [Fact]
    public async Task LoadAsync_with_no_snapshots_exposes_empty_state()
    {
        var viewModel = new AssetHistoryViewModel(new FakeSnapshotReader([]));

        await viewModel.LoadAsync(Guid.NewGuid());

        Assert.False(viewModel.HasHistory);
        Assert.Null(viewModel.SelectedSnapshotDate);
        Assert.NotEmpty(viewModel.EmptyMessage);
    }

    [Fact]
    public async Task LoadAsync_builds_asset_and_composition_series_from_the_selected_snapshot()
    {
        var viewModel = new AssetHistoryViewModel(
            new FakeSnapshotReader(
            [
                new AccountSnapshotSummary(
                    new DateOnly(2026, 11, 9),
                    new DateTimeOffset(2026, 11, 9, 16, 0, 0, TimeSpan.Zero),
                    250_000m,
                    true,
                    1_250_000m,
                    800_000m,
                    1_000_000m,
                    200_000m,
                    2_500)
            ]));

        await viewModel.LoadAsync(Guid.NewGuid());

        Assert.Equal(2, viewModel.AssetSeries.Length);
        Assert.True(viewModel.HasComposition);
        Assert.Equal(2, viewModel.CompositionSeries.Length);
        Assert.False(viewModel.HasCashBalanceGap);
    }

    private static AccountSnapshotSummary Summary(DateOnly date) =>
        new(date, new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), 0m, true, 0m, 0m, 0m, 0m, 0);

    private sealed class FakeSnapshotReader(IReadOnlyList<AccountSnapshotSummary> summaries) : IAccountSnapshotReader
    {
        public Task<AccountSnapshotDetail?> GetSnapshotAsync(
            Guid accountId,
            DateOnly snapshotDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AccountSnapshotDetail?>(null);

        public Task<IReadOnlyList<AccountSnapshotSummary>> GetSummariesAsync(
            Guid accountId,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(summaries);
    }
}
