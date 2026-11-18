using AssetPilot.App.ViewModels;
using AssetPilot.Backtest.Experiments;
using AssetPilot.Backtest.Storage;
using System.IO;

namespace AssetPilot.App.Tests;

public sealed class BacktestExperimentViewModelTests
{
    [Fact]
    public async Task RunAsync_runs_three_take_profit_candidates_without_changing_active_strategy()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var dailyPath = Path.Combine(directory.FullName, "daily.csv");
            var fiveMinutePath = Path.Combine(directory.FullName, "five-minute.csv");
            await File.WriteAllTextAsync(dailyPath, "date,close,a,b,c,high,low,close2\n2026-01-02,100,0,0,0,101,99,100");
            await File.WriteAllTextAsync(fiveMinutePath, "date,time,open,high,low,close\n2026-01-02,09:00,100,101,99,100");
            var store = new CapturingStore();
            var viewModel = new BacktestExperimentViewModel(store)
            {
                DailyCsvPath = dailyPath,
                FiveMinuteCsvPath = fiveMinutePath,
                OutputDirectory = directory.FullName
            };

            await viewModel.RunCommand.ExecuteAsync(ExitParameterExperimentScope.TakeProfitOnly);

            Assert.Equal(3, viewModel.Results.Count);
            Assert.All(viewModel.Results, result => Assert.Equal(5, result.Candidate.MaxHoldingDays));
            Assert.Equal(3, store.SavedRuns.Count);
            Assert.True(File.Exists(viewModel.ExportPath));
            Assert.DoesNotContain("추천", viewModel.StatusMessage);
            Assert.DoesNotContain("적용", viewModel.StatusMessage);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class CapturingStore : IBacktestRunStore
    {
        public List<BacktestRun> SavedRuns { get; } = [];

        public Task SaveAsync(BacktestRun run, CancellationToken cancellationToken = default)
        {
            SavedRuns.Add(run);
            return Task.CompletedTask;
        }

        public Task<BacktestRun?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<BacktestRun?>(null);
        public Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BacktestRunSummary>>([]);
        public Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BacktestRunSummary>>([]);
    }
}
