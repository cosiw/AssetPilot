using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Experiments;
using AssetPilot.Backtest.Strategy;
using AssetPilot.Infrastructure.Backtests;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestExperimentCsvExporterTests : IDisposable
{
    private readonly string outputDirectory = Path.Combine(Path.GetTempPath(), $"assetpilot-experiment-export-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportAsync_writes_one_row_per_saved_candidate_with_actual_mdd()
    {
        var experiment = new BacktestExperimentReport(
            Guid.NewGuid(),
            ExitParameterExperimentScope.TakeProfitOnly,
            [CreateResult(1m), CreateResult(1.5m), CreateResult(2m)]);

        var output = await new BacktestExperimentCsvExporter().ExportAsync(experiment, outputDirectory);
        var lines = await File.ReadAllLinesAsync(output);

        Assert.Equal("ExperimentId,BacktestId,StrategyVersion,StartDate,EndDate,TakeProfitAtrMultiplier,StopLossAtrMultiplier,MaxHoldingDays,TotalReturn,CAGR,MDD,WinRate,TradeCount,ProfitFactor,AverageReturn,AverageWin,AverageLoss,MaxConsecutiveLosses,Error", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains(",0.1,", lines[1]);
    }

    [Fact]
    public async Task ExportAsync_uses_the_full_equity_curve_period_not_the_trade_period_for_dates()
    {
        var result = CreateResult(1m);
        var curve = new BacktestEquityCurve(
        [
            new BacktestEquityPoint(new DateTime(2025, 12, 22, 9, 0, 0), 10_000_000m, 0m, 0m, 0m, 0m, 10_000_000m, 0m, 10_000_000m, 0m, 0m),
            new BacktestEquityPoint(new DateTime(2026, 11, 11, 15, 35, 0), 10_100_000m, 0m, 0m, 0m, 0m, 10_100_000m, 0.01m, 10_100_000m, 0m, 0m)
        ], 0m, 0m, null, null);
        var experiment = new BacktestExperimentReport(
            Guid.NewGuid(),
            ExitParameterExperimentScope.TakeProfitOnly,
            [result with { Result = result.Result! with { EquityCurve = curve }, EquityCurve = curve }]);

        var output = await new BacktestExperimentCsvExporter().ExportAsync(experiment, outputDirectory);
        var fields = (await File.ReadAllLinesAsync(output))[1].Split(',');

        Assert.Equal("2025-12-22T09:00:00.0000000", fields[3]);
        Assert.Equal("2026-11-11T15:35:00.0000000", fields[4]);
    }

    public void Dispose()
    {
        if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
    }

    private static BacktestExperiment CreateResult(decimal takeProfit)
    {
        var candidate = new ExitParameterCandidate(takeProfit, 1m, 5);
        var options = BaselineStrategyOptions.Create(candidate.ToExitOptions());
        var performance = new BacktestPerformanceReport(1, 100m, 0.1m, 0.2m, 1m, 2m, 0.1m, 100m, 0m, 0, [], [], []);
        var curve = new BacktestEquityCurve([], 100m, 0.1m, null, null);
        var result = new BacktestResult([], [], PerformanceReport: performance, EquityCurve: curve);
        var metrics = new BacktestMetrics(1, 1m, 2m, 100m, 100m, 0.1m, 100m, 0m, 0);
        return new BacktestExperiment(candidate, options, result, metrics, performance, curve, null, Guid.NewGuid());
    }
}
