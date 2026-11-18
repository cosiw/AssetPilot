using AssetPilot.App.ViewModels;
using AssetPilot.Backtest.Storage;
using System.IO;

namespace AssetPilot.App.Tests;

public sealed class BacktestRunViewModelTests
{
    [Fact]
    public async Task Run_loads_selected_csv_files_and_publishes_a_result()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var dailyPath = Path.Combine(directory.FullName, "daily.csv");
            var fiveMinutePath = Path.Combine(directory.FullName, "five-minute.csv");
            await File.WriteAllTextAsync(dailyPath, "date,close,a,b,c,high,low,close2\n2026-01-02,100,0,0,0,101,99,100");
            await File.WriteAllTextAsync(fiveMinutePath, "date,time,open,high,low,close\n2026-01-02,09:00,100,101,99,100");
            var viewModel = new BacktestRunViewModel { DailyCsvPath = dailyPath, FiveMinuteCsvPath = fiveMinutePath };
            var completed = false;
            viewModel.RunCompleted += (_, _) => completed = true;

            await viewModel.RunCommand.ExecuteAsync(null);

            Assert.NotNull(viewModel.Result);
            Assert.NotNull(viewModel.Metrics);
            Assert.True(completed);
            Assert.Contains("0", viewModel.StatusMessage);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Run_reports_csv_format_error_without_throwing()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var dailyPath = Path.Combine(directory.FullName, "daily.csv");
            var fiveMinutePath = Path.Combine(directory.FullName, "five-minute.csv");
            await File.WriteAllTextAsync(dailyPath, "date,close\n2026-01-02,100");
            await File.WriteAllTextAsync(fiveMinutePath, "date,time,open,high,low,close\n2026-01-02,09:00,100,101,99,100");
            var viewModel = new BacktestRunViewModel { DailyCsvPath = dailyPath, FiveMinuteCsvPath = fiveMinutePath };

            await viewModel.RunCommand.ExecuteAsync(null);

            Assert.Null(viewModel.Result);
            Assert.Contains("CSV", viewModel.StatusMessage);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Run_rejects_non_positive_starting_cash()
    {
        var viewModel = new BacktestRunViewModel();
        viewModel.StartingCash = 0m;

        viewModel.RunCommand.Execute(null);

        Assert.Equal("시작 자금은 0보다 커야 합니다.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Run_rejects_allocation_outside_one_to_hundred_percent()
    {
        var viewModel = new BacktestRunViewModel { StartingCash = 1_000_000m, AllocationPercent = 0m };

        viewModel.RunCommand.Execute(null);

        Assert.Equal("투입 비율은 0 초과 100 이하여야 합니다.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Run_uses_the_fixed_baseline_entry_rules()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var dailyPath = Path.Combine(directory.FullName, "daily.csv");
            var fiveMinutePath = Path.Combine(directory.FullName, "five-minute.csv");
            await File.WriteAllTextAsync(dailyPath, "date,close,a,b,c,high,low,close2\n2026-01-02,100,0,0,0,101,99,100");
            await File.WriteAllTextAsync(fiveMinutePath, "date,time,open,high,low,close\n2026-01-02,09:00,100,101,99,100");
            var viewModel = new BacktestRunViewModel
            {
                DailyCsvPath = dailyPath,
                FiveMinuteCsvPath = fiveMinutePath
            };

            await viewModel.RunCommand.ExecuteAsync(null);

            Assert.Equal(120, viewModel.ExecutedOptions?.SmaPeriod);
            Assert.Equal(5, viewModel.ExecutedOptions?.ShortSmaPeriod);
            Assert.Equal(14, viewModel.ExecutedOptions?.AtrPeriod);
            Assert.Equal(5, viewModel.ExecutedOptions?.SmaSlopeLookbackDays);
            Assert.Equal(1m, viewModel.ExecutedOptions?.MinimumSmaDistancePercent);
            Assert.Equal(0.5m, viewModel.ExecutedOptions?.BuyBandAtrMultiplier);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Run_summarizes_the_new_performance_report_without_presenting_legacy_mdd_as_final()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var dailyPath = Path.Combine(directory.FullName, "daily.csv");
            var fiveMinutePath = Path.Combine(directory.FullName, "five-minute.csv");
            await File.WriteAllTextAsync(dailyPath, "date,close,a,b,c,high,low,close2\n2026-01-02,100,0,0,0,101,99,100");
            await File.WriteAllTextAsync(fiveMinutePath, "date,time,open,high,low,close\n2026-01-02,09:00,100,101,99,100");
            var viewModel = new BacktestRunViewModel { DailyCsvPath = dailyPath, FiveMinuteCsvPath = fiveMinutePath };

            await viewModel.RunCommand.ExecuteAsync(null);

            Assert.Contains("CAGR", viewModel.MetricsSummary);
            Assert.Contains("MDD 0.00%", viewModel.MetricsSummary);
            Assert.DoesNotContain("실현손익 기준", viewModel.MetricsSummary);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task Run_saves_the_baseline_strategy_version_and_full_options()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var dailyPath = Path.Combine(directory.FullName, "daily.csv");
            var fiveMinutePath = Path.Combine(directory.FullName, "five-minute.csv");
            await File.WriteAllTextAsync(dailyPath, "date,close,a,b,c,high,low,close2\n2026-01-02,100,0,0,0,101,99,100");
            await File.WriteAllTextAsync(fiveMinutePath, "date,time,open,high,low,close\n2026-01-02,09:00,100,101,99,100");
            var store = new CapturingStore();
            var viewModel = new BacktestRunViewModel(store)
            {
                DailyCsvPath = dailyPath,
                FiveMinuteCsvPath = fiveMinutePath,
                TargetAtrMultiplier = 2m,
                StopAtrMultiplier = 0.8m,
                MaxHoldingDays = 7
            };

            await viewModel.RunCommand.ExecuteAsync(null);

            Assert.Equal("swing-atr-baseline-v1", store.SavedRun?.StrategyKey);
            Assert.Contains("\"ShortSmaPeriod\":5", store.SavedRun?.StrategyOptionsJson);
            Assert.Contains("\"TargetAtrMultiplier\":2", store.SavedRun?.StrategyOptionsJson);
            Assert.Contains("\"StopAtrMultiplier\":0.8", store.SavedRun?.StrategyOptionsJson);
            Assert.Contains("\"MaxHoldingDays\":7", store.SavedRun?.StrategyOptionsJson);
            Assert.Equal(viewModel.Result!.PositionSnapshots, store.SavedRun?.PositionSnapshots);
            Assert.Equal(viewModel.Result.CompletedPositionCycles, store.SavedRun?.CompletedPositionCycles);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private sealed class CapturingStore : IBacktestRunStore
    {
        public BacktestRun? SavedRun { get; private set; }

        public Task SaveAsync(BacktestRun run, CancellationToken cancellationToken = default)
        {
            SavedRun = run;
            return Task.CompletedTask;
        }

        public Task<BacktestRun?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<BacktestRun?>(null);

        public Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BacktestRunSummary>>([]);

        public Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BacktestRunSummary>>([]);
    }
}
