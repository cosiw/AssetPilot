using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Experiments;
using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Storage;
using AssetPilot.Backtest.Strategy;
using AssetPilot.Infrastructure.Backtests;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetPilot.App.ViewModels;

public partial class BacktestExperimentViewModel : ObservableObject
{
    private readonly IBacktestRunStore? store;
    private readonly BacktestExperimentCsvExporter exporter;

    public BacktestExperimentViewModel()
        : this(null)
    {
    }

    public BacktestExperimentViewModel(IBacktestRunStore? store, BacktestExperimentCsvExporter? exporter = null)
    {
        this.store = store;
        this.exporter = exporter ?? new BacktestExperimentCsvExporter();
    }

    public ObservableCollection<BacktestExperiment> Results { get; } = [];

    [ObservableProperty]
    public partial string DailyCsvPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FiveMinuteCsvPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal StartingCash { get; set; } = 10_000_000m;

    [ObservableProperty]
    public partial decimal AllocationPercent { get; set; } = 20m;

    [ObservableProperty]
    public partial string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssetPilot", "exports");

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExportPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    private bool CanRun() => !IsRunning;

    partial void OnIsRunningChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRun), AllowConcurrentExecutions = false)]
    private async Task RunAsync(ExitParameterExperimentScope scope)
    {
        if (StartingCash <= 0m)
        {
            StatusMessage = "시작 자금은 0보다 커야 합니다.";
            return;
        }

        if (AllocationPercent <= 0m || AllocationPercent > 100m)
        {
            StatusMessage = "투자 비율은 0 초과 100 이하이어야 합니다.";
            return;
        }

        if (!File.Exists(DailyCsvPath) || !File.Exists(FiveMinuteCsvPath))
        {
            StatusMessage = "일봉과 5분봉 CSV 파일 경로를 확인하세요.";
            return;
        }

        try
        {
            IsRunning = true;
            StatusMessage = "청산 파라미터 실험을 실행 중입니다.";
            ExportPath = string.Empty;
            Results.Clear();

            var loader = new CsvMarketDataLoader();
            var dailyBars = loader.LoadDaily(DailyCsvPath);
            var fiveMinuteBars = loader.LoadFiveMinute(FiveMinuteCsvPath);
            var request = new BacktestRequest(
                dailyBars,
                fiveMinuteBars,
                StartingCash,
                AllocationPercent / 100m,
                AnalysisOptions: new BacktestAnalysisOptions());
            var report = new BacktestExperimentRunner().Run(request, scope);
            var storedResults = new List<BacktestExperiment>(report.Results.Count);

            foreach (var experiment in report.Results)
            {
                if (store is null || experiment.Result is null || experiment.Metrics is null)
                {
                    storedResults.Add(experiment);
                    continue;
                }

                var run = new BacktestRun(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    BaselineStrategyOptions.StrategyVersion,
                    "ATR 단일 진입 기준 전략 청산 파라미터 실험",
                    JsonSerializer.Serialize(experiment.StrategyOptions),
                    "KODEX 200",
                    StartingCash,
                    StartingCash + experiment.Metrics.TotalProfit,
                    experiment.Metrics.TotalProfit,
                    experiment.Metrics.ReturnRate,
                    experiment.Result.Executions,
                    experiment.Result.Diagnostics,
                    experiment.Result.PositionSnapshots,
                    experiment.Result.CompletedPositionCycles,
                    dailyBars.Min(bar => bar.Date),
                    dailyBars.Max(bar => bar.Date),
                    request.AnalysisOptions,
                    experiment.Result.Excursions,
                    experiment.Result.PerformanceReport,
                    experiment.Result.EquityCurve,
                    report.ExperimentId);
                await store.SaveAsync(run);
                storedResults.Add(experiment with { BacktestId = run.Id });
            }

            report = report with { Results = storedResults };
            foreach (var result in report.Results) Results.Add(result);
            ExportPath = await exporter.ExportAsync(report, OutputDirectory);
            StatusMessage = $"실험 완료: 후보 {Results.Count}개, 저장 {storedResults.Count(result => result.BacktestId.HasValue)}개";
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Results.Clear();
            ExportPath = string.Empty;
            StatusMessage = $"실험 실패: {exception.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
