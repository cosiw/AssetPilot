using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Storage;
using AssetPilot.Infrastructure.Backtests;
using Microsoft.Data.Sqlite;

namespace AssetPilot.Backtest.Tests;

public sealed class SqliteBacktestRunStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"assetpilot-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveAsync_round_trips_a_run_and_its_executions()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        var run = new BacktestRun(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-11-12T00:00:00Z"), "swing-atr", "ATR 단일 진입",
            "{}", "KODEX 200", 1_000_000m, 1_060_000m, 60_000m, 0.06m,
            new[] { new BacktestExecution(new DateTime(2026, 1, 2, 9, 0, 0), ExecutionSide.Buy, 10_000m, 100m) },
            new[] { new BacktestDiagnostic("MISSING_INTRADAY", new DateOnly(2026, 1, 3), "5-minute data is missing.") });

        await store.SaveAsync(run);
        var saved = await store.GetAsync(run.Id);

        Assert.NotNull(saved);
        Assert.Equal(run.StrategyKey, saved.StrategyKey);
        Assert.Equal(60_000m, saved.TotalProfit);
        Assert.Single(saved.Executions);
        var diagnostic = Assert.Single(saved!.Diagnostics!);
        Assert.Equal("MISSING_INTRADAY", diagnostic.Code);
    }

    [Fact]
    public async Task SaveAsync_round_trips_the_optional_experiment_id()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        var experimentId = Guid.NewGuid();
        var run = CreateRun("2026-11-18T00:00:00Z") with { ExperimentId = experimentId };

        await store.SaveAsync(run);
        var saved = await store.GetAsync(run.Id);

        Assert.Equal(experimentId, saved!.ExperimentId);
    }

    [Fact]
    public async Task SaveAsync_round_trips_execution_snapshots_and_completed_cycles()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        var buyId = Guid.NewGuid();
        var sellId = Guid.NewGuid();
        var run = new BacktestRun(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-11-16T00:00:00Z"), "baseline-v1", "Baseline", "{}", "KODEX 200",
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
            CompletedPositionCycles: new[]
            {
                new BacktestPositionCycle(Guid.NewGuid(), buyId, sellId, new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 2, 10, 0, 0), 100m, 100m, 10_000m, 10_600m, 60_000m, 0.06m, ExitReason.Target)
            });

        await store.SaveAsync(run);
        var saved = await store.GetAsync(run.Id);

        Assert.NotNull(saved);
        Assert.Equal(buyId, saved.Executions[0].ExecutionId);
        Assert.Equal(2, saved.Executions[1].Sequence);
        Assert.Equal(1_060_000m, saved.Executions[1].GrossAmount);
        Assert.Equal(60_000m, saved.Executions[1].RealizedProfit);
        Assert.Equal(1_060_000m, saved.PositionSnapshotsOrEmpty[1].Cash);
        Assert.Equal(ExitReason.Target, Assert.Single(saved.CompletedPositionCyclesOrEmpty).ClosingReason);
    }

    [Fact]
    public async Task SaveAsync_round_trips_analysis_inputs_excursions_and_recreated_performance_report()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        var openingId = Guid.NewGuid();
        var closingId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var run = new BacktestRun(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-11-17T00:00:00Z"), "baseline-v1", "Baseline", "{}", "KODEX 200",
            1_000m, 1_100m, 100m, 0.1m, [],
            CompletedPositionCycles:
            [
                new BacktestPositionCycle(cycleId, openingId, closingId, new DateTime(2026, 1, 2, 9, 0, 0), new DateTime(2026, 1, 10, 9, 0, 0), 1m, 1m, 1_000m, 1_100m, 100m, 0.1m, ExitReason.Target)
            ],
            DataStartDate: new DateOnly(2026, 1, 1),
            DataEndDate: new DateOnly(2026, 1, 31),
            AnalysisOptions: new BacktestAnalysisOptions(3),
            ExcursionResults:
            [
                new BacktestExcursion(cycleId, 120m, 0.12m, -30m, -0.03m, 150m, 0.15m, -40m, -0.04m, 0.2m, -0.1m, 3)
            ]);

        await store.SaveAsync(run);
        var saved = await store.GetAsync(run.Id);

        Assert.NotNull(saved);
        Assert.Equal(new DateOnly(2026, 1, 1), saved!.DataStartDate);
        Assert.Equal(3, saved.AnalysisOptions!.FixedObservationTradingDays);
        Assert.Equal(0.12m, Assert.Single(saved.Excursions).HoldingMfeRate);
        Assert.NotNull(saved.PerformanceReport);
        Assert.Equal(1, saved.PerformanceReport!.CompletedTrades);
        Assert.True(saved.PerformanceReport.Cagr > 0m);
    }

    [Fact]
    public async Task SaveAsync_round_trips_mark_to_market_equity_curve()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        var run = new BacktestRun(
            Guid.NewGuid(), DateTimeOffset.Parse("2026-11-18T00:00:00Z"), "baseline-v1", "Baseline", "{}", "KODEX 200",
            1_000m, 900m, -100m, -0.1m, [],
            EquityCurve: new BacktestEquityCurve(
            [
                new BacktestEquityPoint(new DateTime(2026, 1, 2, 9, 0, 0), 500m, 5m, 100m, 100m, 500m, 1_000m, 0m, 1_000m, 0m, 0m),
                new BacktestEquityPoint(new DateTime(2026, 1, 2, 9, 5, 0), 500m, 5m, 100m, 80m, 400m, 900m, -0.1m, 1_000m, -100m, -0.1m)
            ],
            100m,
            0.1m,
            new DateTime(2026, 1, 2, 9, 0, 0),
            new DateTime(2026, 1, 2, 9, 5, 0)));

        await store.SaveAsync(run);
        var saved = await store.GetAsync(run.Id);

        var curve = Assert.IsType<BacktestEquityCurve>(saved!.EquityCurve);
        Assert.Equal(2, curve.Points.Count);
        Assert.Equal(900m, curve.Points[1].TotalEquity);
        Assert.Equal(-100m, curve.Points[1].DrawdownAmount);
        Assert.Equal(100m, curve.MaxDrawdownAmount);
        Assert.Equal(0.1m, curve.MaxDrawdownRate);
        Assert.Equal(new DateTime(2026, 1, 2, 9, 0, 0), curve.PeakAt);
        Assert.Equal(new DateTime(2026, 1, 2, 9, 5, 0), curve.TroughAt);
    }

    [Fact]
    public async Task InitializeAsync_upgrades_an_existing_execution_table_without_losing_legacy_rows()
    {
        await using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE backtest_runs (id TEXT PRIMARY KEY, executed_at TEXT NOT NULL, strategy_key TEXT NOT NULL, strategy_name TEXT NOT NULL, options_json TEXT NOT NULL, symbol TEXT NOT NULL, starting_cash TEXT NOT NULL, ending_cash TEXT NOT NULL, total_profit TEXT NOT NULL, return_rate TEXT NOT NULL); CREATE TABLE backtest_executions (run_id TEXT NOT NULL, executed_at TEXT NOT NULL, side INTEGER NOT NULL, price TEXT NOT NULL, quantity TEXT NOT NULL, realized_profit TEXT NULL, reason INTEGER NULL); CREATE TABLE backtest_diagnostics (run_id TEXT NOT NULL, code TEXT NOT NULL, trade_date TEXT NOT NULL, message TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        await store.InitializeAsync();
        await store.SaveAsync(CreateRun("2026-01-03T00:00:00Z"));

        Assert.Single(await store.GetSummariesAsync());
    }

    [Fact]
    public async Task GetSummariesAsync_returns_latest_runs_first()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        await store.SaveAsync(CreateRun("2026-01-01T00:00:00Z"));
        var latest = CreateRun("2026-01-02T00:00:00Z");
        await store.SaveAsync(latest);

        var summaries = await store.GetSummariesAsync();

        Assert.Equal(latest.Id, summaries[0].Id);
    }

    [Fact]
    public async Task GetSummariesAsync_with_ids_returns_only_selected_runs()
    {
        var store = new SqliteBacktestRunStore($"Data Source={_path}");
        await store.InitializeAsync();
        var first = CreateRun("2026-01-01T00:00:00Z");
        var selected = CreateRun("2026-01-02T00:00:00Z");
        await store.SaveAsync(first);
        await store.SaveAsync(selected);

        var summaries = await store.GetSummariesAsync(new[] { selected.Id });

        var summary = Assert.Single(summaries);
        Assert.Equal(selected.Id, summary.Id);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private static BacktestRun CreateRun(string executedAt) => new(Guid.NewGuid(), DateTimeOffset.Parse(executedAt), "swing-atr", "ATR", "{}", "KODEX 200", 1_000m, 1_100m, 100m, 0.1m, []);
}
