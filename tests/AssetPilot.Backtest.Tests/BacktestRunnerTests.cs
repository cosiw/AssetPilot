using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Strategy;
using System.IO;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestRunnerTests
{
    [Fact]
    public void Run_assigns_ordered_executions_and_derives_a_closed_cycle()
    {
        var result = Run(new[]
        {
            Bar("2026-01-02", 10_000m, 10_100m, 9_900m),
            Bar("2026-01-02", 10_000m, 10_600m, 9_900m),
        });

        Assert.Collection(result.Executions,
            buy => { Assert.Equal(1, buy.Sequence); Assert.Equal(ExecutionSide.Buy, buy.Side); },
            sell => { Assert.Equal(2, sell.Sequence); Assert.Equal(ExecutionSide.Sell, sell.Side); });
        Assert.Equal(2, result.PositionSnapshots.Count);
        var cycle = Assert.Single(result.CompletedPositionCycles);
        Assert.NotNull(result.PerformanceReport);
        Assert.Equal(1, result.PerformanceReport.CompletedTrades);
        var excursion = Assert.Single(result.Excursions);
        Assert.Equal(cycle.CycleId, excursion.CycleId);
        var equityCurve = Assert.IsType<BacktestEquityCurve>(result.EquityCurve);
        Assert.Equal(2, equityCurve.Points.Count);
        Assert.True(equityCurve.MaxDrawdownRate >= 0m);
    }

    [Fact]
    public void Run_records_target_profit_using_cash_allocation_quantity()
    {
        var result = Run(new[]
        {
            Bar("2026-01-02", 10_000m, 10_100m, 9_900m),
            Bar("2026-01-02", 10_000m, 10_600m, 9_900m),
        });

        var sale = Assert.Single(result.Executions, x => x.Side == ExecutionSide.Sell);
        Assert.Equal(10_600m, sale.Price);
        Assert.Equal(60_000m, sale.RealizedProfit);
    }

    [Fact]
    public void Run_exits_at_open_when_stop_is_gapped_through()
    {
        var result = Run(new[]
        {
            Bar("2026-01-02", 10_000m, 10_100m, 9_900m),
            Bar("2026-01-02", 9_300m, 9_400m, 9_200m),
        });

        var sale = Assert.Single(result.Executions, x => x.Side == ExecutionSide.Sell);
        Assert.Equal(9_300m, sale.Price);
        Assert.Equal(ExitReason.Stop, sale.Reason);
    }

    [Fact]
    public void Run_uses_stop_when_both_prices_are_touched_in_one_bar()
    {
        var result = Run(new[] { Bar("2026-01-02", 10_000m, 10_600m, 9_600m) });

        var sale = Assert.Single(result.Executions, x => x.Side == ExecutionSide.Sell);
        Assert.Equal(9_600m, sale.Price);
        Assert.Equal(ExitReason.Stop, sale.Reason);
    }

    [Fact]
    public void Run_exits_an_open_position_using_the_next_trading_days_intraday_bars()
    {
        var result = new BacktestRunner().Run(
            new BacktestRequest(
                new[]
                {
                    new DailyBar(new DateOnly(2026, 1, 1), 10_000m, 10_100m, 9_900m, 10_000m),
                    new DailyBar(new DateOnly(2026, 1, 2), 10_000m, 10_100m, 9_900m, 10_000m),
                    new DailyBar(new DateOnly(2026, 1, 3), 10_000m, 10_100m, 9_900m, 10_000m),
                },
                new[]
                {
                    Bar("2026-01-02", 10_000m, 10_100m, 9_900m),
                    Bar("2026-01-03", 10_000m, 10_600m, 9_900m),
                },
                1_000_000m,
                1m),
            new FixedStrategy());

        var sale = Assert.Single(result.Executions, x => x.Side == ExecutionSide.Sell);
        Assert.Equal(new DateTime(2026, 1, 3, 9, 0, 0), sale.ExecutedAt);
        Assert.Equal(ExitReason.Target, sale.Reason);
    }

    [Fact]
    public void Run_closes_an_open_position_at_the_last_bar_of_the_fifth_holding_day()
    {
        var result = new BacktestRunner().Run(
            new BacktestRequest(
                new[]
                {
                    new DailyBar(new DateOnly(2026, 1, 1), 10_000m, 10_100m, 9_900m, 10_000m),
                    new DailyBar(new DateOnly(2026, 1, 2), 10_000m, 10_100m, 9_900m, 10_000m),
                    new DailyBar(new DateOnly(2026, 1, 3), 10_000m, 10_100m, 9_900m, 10_000m),
                    new DailyBar(new DateOnly(2026, 1, 4), 10_000m, 10_100m, 9_900m, 10_000m),
                    new DailyBar(new DateOnly(2026, 1, 5), 10_000m, 10_100m, 9_900m, 10_000m),
                    new DailyBar(new DateOnly(2026, 1, 6), 10_000m, 10_100m, 9_900m, 10_000m),
                },
                new[]
                {
                    Bar("2026-01-02", 10_000m, 10_100m, 9_900m),
                    Bar("2026-01-03", 10_000m, 10_100m, 9_900m),
                    Bar("2026-01-04", 10_000m, 10_100m, 9_900m),
                    Bar("2026-01-05", 10_000m, 10_100m, 9_900m),
                    new FiveMinuteBar(new DateTime(2026, 1, 6, 15, 30, 0), 10_050m, 10_100m, 9_900m, 10_050m),
                },
                1_000_000m,
                1m),
            new FixedStrategy());

        var sale = Assert.Single(result.Executions, x => x.Side == ExecutionSide.Sell);
        Assert.Equal(new DateTime(2026, 1, 6, 15, 30, 0), sale.ExecutedAt);
        Assert.Equal(10_050m, sale.Price);
    }

    [Fact]
    public void Run_sample_market_data_with_single_atr_strategy_can_reenter_after_an_exit()
    {
        var root = FindRepositoryRoot();
        var loader = new CsvMarketDataLoader();
        var result = new BacktestRunner().Run(
            new BacktestRequest(
                loader.LoadDaily(Path.Combine(root, "samples", "market-data", "KODEX 200.csv")),
                loader.LoadFiveMinute(Path.Combine(root, "samples", "market-data", "5분봉(26년).csv")),
                10_000_000m,
                0.2m),
            new SwingAtrStrategy(new StrategyOptions
            {
                SmaPeriod = 140,
                AtrPeriod = 5,
                BuyBandAtrMultiplier = 0.5m,
                TargetAtrMultiplier = 1.5m,
                StopAtrMultiplier = 1m
            }));

        Assert.True(result.Executions.Count(execution => execution.Side == ExecutionSide.Buy) > 1);
    }

    private static BacktestResult Run(IReadOnlyList<FiveMinuteBar> intraday) => new BacktestRunner().Run(
        new BacktestRequest(
            new[]
            {
                new DailyBar(new DateOnly(2026, 1, 1), 10_000m, 10_100m, 9_900m, 10_000m),
                new DailyBar(new DateOnly(2026, 1, 2), 10_000m, 10_100m, 9_900m, 10_000m),
            },
            intraday,
            1_000_000m,
            1m),
        new FixedStrategy());

    private static FiveMinuteBar Bar(string date, decimal open, decimal high, decimal low) =>
        new(DateTime.Parse($"{date} 09:00:00"), open, high, low, open);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AssetPilot.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("AssetPilot repository root was not found.");
    }

    private sealed class FixedStrategy : ITradingStrategy
    {
        public string Key => "fixed";
        public string DisplayName => "Fixed";
        public EntryPlan? EvaluateAtClose(IReadOnlyList<DailyBar> _) => new(new DateOnly(2026, 1, 1), 9_900m, 10_100m, 400m);
        public ExitPlan CreateExitPlan(decimal _, decimal __) => new(10_600m, 9_600m);
    }
}
