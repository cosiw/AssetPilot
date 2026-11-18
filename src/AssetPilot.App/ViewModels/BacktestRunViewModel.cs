using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Strategy;
using AssetPilot.Backtest.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text.Json;

namespace AssetPilot.App.ViewModels;

public partial class BacktestRunViewModel : ObservableObject
{
    private readonly IBacktestRunStore? _store;

    public event EventHandler? RunCompleted;

    public BacktestRunViewModel() { }

    public BacktestRunViewModel(IBacktestRunStore store) => _store = store;
    [ObservableProperty]
    public partial string DailyCsvPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FiveMinuteCsvPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal StartingCash { get; set; } = 10_000_000m;

    [ObservableProperty]
    public partial decimal AllocationPercent { get; set; } = 20m;

    [ObservableProperty]
    public partial decimal TargetAtrMultiplier { get; set; } = 1.5m;

    [ObservableProperty]
    public partial decimal StopAtrMultiplier { get; set; } = 1.0m;

    [ObservableProperty]
    public partial int MaxHoldingDays { get; set; } = 5;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial BacktestResult? Result { get; set; }

    [ObservableProperty]
    public partial BacktestExecution? SelectedExecution { get; set; }

    [ObservableProperty]
    public partial BacktestMetrics? Metrics { get; set; }

    [ObservableProperty]
    public partial BacktestPerformanceReport? PerformanceReport { get; set; }

    [ObservableProperty]
    public partial StrategyOptions? ExecutedOptions { get; set; }

    [ObservableProperty]
    public partial string MetricsSummary { get; set; } = string.Empty;

    partial void OnMetricsChanged(BacktestMetrics? value)
    {
        MetricsSummary = value is null
            ? string.Empty
            : $"완료 {value.CompletedTrades:N0}건 | 승률 {value.WinRate:P2} | PF {value.ProfitFactor:N2} | MDD {value.MaxDrawdown:N0} | 평균 수익 {value.AverageWin:N0} | 평균 손실 {value.AverageLoss:N0} | 최대 연속 손실 {value.MaxConsecutiveLosses:N0}";
    }

    partial void OnPerformanceReportChanged(BacktestPerformanceReport? value)
    {
        if (value is null)
        {
            return;
        }

        var actualMdd = Result?.EquityCurve?.MaxDrawdownRate ?? 0m;
        MetricsSummary = $"완료 {value.CompletedTrades:N0}건 | CAGR {value.Cagr:P2} | 승률 {value.WinRate:P2} | PF {value.ProfitFactor:N2} | 평균 수익률 {value.AverageReturn:P2} | 최대 연속 손실 {value.MaxConsecutiveLosses:N0} | MDD {actualMdd:P2}";
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (StartingCash <= 0m)
        {
            StatusMessage = "시작 자금은 0보다 커야 합니다.";
            return;
        }

        if (AllocationPercent <= 0m || AllocationPercent > 100m)
        {
            StatusMessage = "투입 비율은 0 초과 100 이하여야 합니다.";
            return;
        }

        if (TargetAtrMultiplier <= 0m || StopAtrMultiplier <= 0m)
        {
            StatusMessage = "ATR 배수는 올바른 값이어야 합니다.";
            return;
        }

        if (MaxHoldingDays <= 0)
        {
            StatusMessage = "최대 보유 기간은 1거래일 이상이어야 합니다.";
            return;
        }

        if (!File.Exists(DailyCsvPath) || !File.Exists(FiveMinuteCsvPath))
        {
            StatusMessage = "일봉과 5분봉 CSV 파일 경로를 확인하세요.";
            return;
        }

        try
        {
            var loader = new CsvMarketDataLoader();
            var dailyBars = loader.LoadDaily(DailyCsvPath);
            var fiveMinuteBars = loader.LoadFiveMinute(FiveMinuteCsvPath);
            var exitOptions = new ExitStrategyOptions(
                TargetAtrMultiplier,
                StopAtrMultiplier,
                MaxHoldingDays);
            ExecutedOptions = BaselineStrategyOptions.Create(exitOptions);
            var analysisOptions = new BacktestAnalysisOptions();
            Result = new BacktestRunner().Run(
                new BacktestRequest(dailyBars, fiveMinuteBars, StartingCash, AllocationPercent / 100m, exitOptions.MaxHoldingDays, analysisOptions),
                new SwingAtrStrategy(ExecutedOptions));
            Metrics = BacktestMetricsCalculator.Calculate(StartingCash, Result.Executions);
            PerformanceReport = Result.PerformanceReport;
            if (_store is not null)
            {
                await _store.SaveAsync(new BacktestRun(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    BaselineStrategyOptions.StrategyVersion,
                    "ATR 단일 진입 기준 전략",
                    JsonSerializer.Serialize(ExecutedOptions),
                    "KODEX 200",
                    StartingCash,
                    StartingCash + Metrics.TotalProfit,
                    Metrics.TotalProfit,
                    Metrics.ReturnRate,
                    Result.Executions,
                    Result.Diagnostics,
                    Result.PositionSnapshots,
                    Result.CompletedPositionCycles,
                    dailyBars.Count == 0 ? null : dailyBars.Min(bar => bar.Date),
                    dailyBars.Count == 0 ? null : dailyBars.Max(bar => bar.Date),
                    analysisOptions,
                    Result.Excursions,
                    Result.PerformanceReport,
                    Result.EquityCurve));
            }

            StatusMessage = Result.Executions.Count == 0
                ? "백테스트는 완료됐지만 체결 0건입니다. 일봉이 SMA 위에 있고, 다음 거래일 5분봉이 매수 구간에 닿았는지 확인하세요."
                : $"백테스트 완료: 체결 {Result.Executions.Count:N0}건 (비용 미반영)";
            RunCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (FormatException exception)
        {
            Result = null;
            Metrics = null;
            StatusMessage = $"CSV 형식을 확인하세요. {exception.Message}";
        }
        catch (IOException exception)
        {
            Result = null;
            Metrics = null;
            StatusMessage = $"CSV 파일을 읽을 수 없습니다. {exception.Message}";
        }
    }
}
