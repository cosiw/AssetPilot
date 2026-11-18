using System.Text;
using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Storage;
using AssetPilot.Infrastructure.Backtests;
using Microsoft.Data.Sqlite;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestRunCsvExporterTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"assetpilot-export-{Guid.NewGuid():N}.db");
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"assetpilot-export-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportAsync_writes_separate_ordered_buy_and_sell_rows_with_stable_headers()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_databasePath}");
        await store.InitializeAsync();
        var run = CreateRun();
        await store.SaveAsync(run);
        var exporter = new BacktestRunCsvExporter(store);

        var files = await exporter.ExportAsync(run.Id, _outputDirectory);

        var summaryLines = await File.ReadAllLinesAsync(files.SummaryPath);
        Assert.Equal("BacktestId,StrategyVersion,StartDate,EndDate,TakeProfitAtrMultiplier,StopLossAtrMultiplier,MaxHoldingDays,TotalReturn,CAGR,MDD,WinRate,TradeCount,ProfitFactor,AverageReturn", summaryLines[0]);
        Assert.Contains(",0.06,", summaryLines[1]);
        Assert.Contains(",0.1,", summaryLines[1]);
        Assert.Contains(",1,", summaryLines[1]);
        var executionLines = await File.ReadAllLinesAsync(files.ExecutionsPath);
        Assert.Equal("BacktestId,ExecutionId,ExecutionSequence,Symbol,DateTime,Side,Price,Quantity,GrossAmount,Fee,Tax,RealizedPnL,ExitReason,StrategyVersion", executionLines[0]);
        Assert.Contains(",Buy,10000,100,1000000,0,0,,", executionLines[1]);
        Assert.Contains(",Sell,10600,100,1060000,0,0,60000,Target,", executionLines[2]);
        Assert.Equal("BacktestId,DateTime,Cash,PositionQuantity,AverageEntryPrice,MarkPrice,PositionValue,TotalEquity,CumulativeReturn,PeakEquity,DrawdownAmount,DrawdownRate", (await File.ReadAllLinesAsync(files.EquityPath))[0]);
        var cycleLines = await File.ReadAllLinesAsync(files.PositionCyclesPath);
        Assert.Equal("BacktestId,CycleId,OpenedAt,ClosedAt,BoughtQuantity,SoldQuantity,AverageEntryPrice,AverageExitPrice,RealizedPnL,ReturnRate,ClosingReason,HoldingMfeRate,HoldingMaeRate,FixedMfeRate,FixedMaeRate,PostExitMfeRate,PostExitMaeRate", cycleLines[0]);
        Assert.Contains(",0.12,-0.03,0.15,-0.04,0.2,-0.1", cycleLines[1]);
        Assert.Equal("BacktestId,ExitReason,TradeCount,TotalProfit,WinRate,AverageReturn", (await File.ReadAllLinesAsync(files.ExitReasonPerformancePath))[0]);
        Assert.Equal("BacktestId,Month,TradeCount,TotalProfit,WinRate,AverageReturn", (await File.ReadAllLinesAsync(files.MonthlyPerformancePath))[0]);
        Assert.Equal(Encoding.UTF8.GetPreamble(), (await File.ReadAllBytesAsync(files.ExecutionsPath))[..3]);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        if (Directory.Exists(_outputDirectory)) Directory.Delete(_outputDirectory, true);
    }

    private static BacktestRun CreateRun()
    {
        var buyId = Guid.NewGuid();
        var sellId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        return new BacktestRun(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-11-16T00:00:00Z"), "swing-atr-baseline-v1", "Baseline", "{\"TargetAtrMultiplier\":1.5,\"StopAtrMultiplier\":1,\"MaxHoldingDays\":5}", "KODEX 200",
            1_000_000m, 1_060_000m, 60_000m, 0.06m,
            new[]
            {
                new BacktestExecution(buyId, 1, new DateTime(2026, 1, 2, 9, 0, 0), ExecutionSide.Buy, 10_000m, 100m, 1_000_000m, 0m, 0m),
                new BacktestExecution(sellId, 2, new DateTime(2026, 1, 2, 10, 0, 0), ExecutionSide.Sell, 10_600m, 100m, 1_060_000m, 0m, 0m, 60_000m, ExitReason.Target)
            },
            PositionSnapshots: new[]
            {
                new BacktestPositionSnapshot(buyId, 1, new DateTime(2026, 1, 2, 9, 0, 0), 0m, 100m, 10_000m, 1_000_000m, 0m),
                new BacktestPositionSnapshot(sellId, 2, new DateTime(2026, 1, 2, 10, 0, 0), 1_060_000m, 0m, 0m, 0m, 60_000m)
            },
            CompletedPositionCycles:
            [
                new BacktestPositionCycle(cycleId, buyId, sellId, new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0), 100m, 100m, 10_000m, 10_600m, 60_000m, 0.06m, ExitReason.Target)
            ],
            DataStartDate: new DateOnly(2026, 1, 1),
            DataEndDate: new DateOnly(2026, 1, 31),
            AnalysisOptions: new BacktestAnalysisOptions(5),
            ExcursionResults:
            [
                new BacktestExcursion(cycleId, 1_200m, 0.12m, -300m, -0.03m, 1_500m, 0.15m, -400m, -0.04m, 0.2m, -0.1m, 5)
            ],
            EquityCurve: new BacktestEquityCurve(
            [
                new BacktestEquityPoint(new DateTime(2026, 1, 2, 9, 0, 0), 500_000m, 50m, 10_000m, 10_000m, 500_000m, 1_000_000m, 0m, 1_000_000m, 0m, 0m),
                new BacktestEquityPoint(new DateTime(2026, 1, 2, 9, 5, 0), 500_000m, 50m, 10_000m, 8_000m, 400_000m, 900_000m, -0.1m, 1_000_000m, -100_000m, -0.1m)
            ],
            100_000m,
            0.1m,
            new DateTime(2026, 1, 2, 9, 0, 0),
            new DateTime(2026, 1, 2, 9, 5, 0)));
    }
}
