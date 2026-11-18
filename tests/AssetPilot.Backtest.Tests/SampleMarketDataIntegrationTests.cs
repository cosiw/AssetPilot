using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Experiments;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Tests;

public sealed class SampleMarketDataIntegrationTests
{
    [Fact]
    public void Sample_exit_parameter_experiment_runs_three_fixed_entry_take_profit_candidates()
    {
        var root = FindRepositoryRoot();
        var samples = Path.Combine(root, "samples", "market-data");
        var loader = new CsvMarketDataLoader();
        var daily = loader.LoadDaily(Path.Combine(samples, "KODEX 200.csv"));
        var intraday = loader.LoadFiveMinute(Path.Combine(samples, "5\uBD84\uBD09(26\uB144).csv"));

        var report = new BacktestExperimentRunner().Run(
            new BacktestRequest(daily, intraday, 10_000_000m, 0.2m),
            ExitParameterExperimentScope.TakeProfitOnly);

        Assert.Equal(3, report.Results.Count);
        Assert.All(report.Results, result =>
        {
            Assert.Null(result.Error);
            Assert.Equal(120, result.StrategyOptions.SmaPeriod);
            Assert.Equal(5, result.StrategyOptions.ShortSmaPeriod);
            Assert.Equal(14, result.StrategyOptions.AtrPeriod);
            Assert.Equal(5, result.StrategyOptions.SmaSlopeLookbackDays);
            Assert.Equal(1m, result.StrategyOptions.MinimumSmaDistancePercent);
            Assert.Equal(0.5m, result.StrategyOptions.BuyBandAtrMultiplier);
            var curve = Assert.IsType<AssetPilot.Backtest.Analysis.BacktestEquityCurve>(result.EquityCurve);
            Assert.Equal(intraday.Count, curve.Points.Count);
            Assert.True(curve.MaxDrawdownRate >= 0m);
        });
    }

    [Fact]
    public void Sample_backtest_keeps_one_position_snapshot_per_execution()
    {
        var root = FindRepositoryRoot();
        var samples = Path.Combine(root, "samples", "market-data");
        var loader = new CsvMarketDataLoader();
        var result = new BacktestRunner().Run(
            new BacktestRequest(
                loader.LoadDaily(Path.Combine(samples, "KODEX 200.csv")),
                loader.LoadFiveMinute(Path.Combine(samples, "5\uBD84\uBD09(26\uB144).csv")),
                10_000_000m,
                0.2m),
            new SwingAtrStrategy(new StrategyOptions()));

        Assert.Equal(result.Executions.Count, result.PositionSnapshots.Count);
        Assert.Equal(Enumerable.Range(1, result.Executions.Count), result.Executions.Select(execution => execution.Sequence));
        foreach (var cycle in result.CompletedPositionCycles)
        {
            var closingSnapshot = result.PositionSnapshots.Single(snapshot => snapshot.ExecutionId == cycle.ClosingExecutionId);
            Assert.Equal(0m, closingSnapshot.Quantity);
        }
    }

    [Fact]
    public void Sample_backtest_creates_one_excursion_per_completed_position_cycle()
    {
        var root = FindRepositoryRoot();
        var samples = Path.Combine(root, "samples", "market-data");
        var loader = new CsvMarketDataLoader();
        var result = new BacktestRunner().Run(
            new BacktestRequest(
                loader.LoadDaily(Path.Combine(samples, "KODEX 200.csv")),
                loader.LoadFiveMinute(Path.Combine(samples, "5\uBD84\uBD09(26\uB144).csv")),
                10_000_000m,
                0.2m),
            new SwingAtrStrategy(new StrategyOptions()));

        Assert.NotEmpty(result.CompletedPositionCycles);
        Assert.NotNull(result.PerformanceReport);
        Assert.Equal(result.CompletedPositionCycles.Count, result.PerformanceReport!.CompletedTrades);
        Assert.Equal(result.CompletedPositionCycles.Count, result.Excursions.Count);
        Assert.Equal(
            result.CompletedPositionCycles.Select(cycle => cycle.CycleId).OrderBy(id => id),
            result.Excursions.Select(excursion => excursion.CycleId).OrderBy(id => id));
        Assert.All(result.Excursions, excursion => Assert.Equal(5, excursion.FixedObservationTradingDays));
    }

    [Fact]
    public void Sample_backtest_creates_a_mark_to_market_equity_point_for_every_five_minute_bar()
    {
        var root = FindRepositoryRoot();
        var samples = Path.Combine(root, "samples", "market-data");
        var loader = new CsvMarketDataLoader();
        var fiveMinuteBars = loader.LoadFiveMinute(Path.Combine(samples, "5\uBD84\uBD09(26\uB144).csv"));
        var result = new BacktestRunner().Run(
            new BacktestRequest(
                loader.LoadDaily(Path.Combine(samples, "KODEX 200.csv")),
                fiveMinuteBars,
                10_000_000m,
                0.2m),
            new SwingAtrStrategy(new StrategyOptions()));

        var equityCurve = Assert.IsType<AssetPilot.Backtest.Analysis.BacktestEquityCurve>(result.EquityCurve);
        Assert.Equal(fiveMinuteBars.Count, equityCurve.Points.Count);
        Assert.All(equityCurve.Points, point => Assert.True(point.TotalEquity >= 0m));
        Assert.True(equityCurve.MaxDrawdownRate >= 0m);
    }

    [Fact]
    public void Trend_filters_can_be_compared_with_the_unfiltered_atr_baseline_on_the_provided_sample()
    {
        var root = FindRepositoryRoot();
        var samples = Path.Combine(root, "samples", "market-data");
        var loader = new CsvMarketDataLoader();
        var daily = loader.LoadDaily(Path.Combine(samples, "KODEX 200.csv"));
        var intraday = loader.LoadFiveMinute(Path.Combine(samples, "5분봉(26년).csv"));
        const decimal startingCash = 10_000_000m;
        const decimal allocationRatio = 0.2m;
        var options = new StrategyOptions();
        var request = new BacktestRequest(daily, intraday, startingCash, allocationRatio, options.MaxHoldingDays);

        var baseline = new BacktestRunner().Run(request, new UnfilteredSwingAtrStrategy(options));
        var filtered = new BacktestRunner().Run(request, new SwingAtrStrategy(options));
        var baselineMetrics = BacktestMetricsCalculator.Calculate(startingCash, baseline.Executions);
        var filteredMetrics = BacktestMetricsCalculator.Calculate(startingCash, filtered.Executions);

        WriteComparison("기본값 (SMA 120 / ATR 14)", baseline, baselineMetrics, filtered, filteredMetrics);

        Assert.NotEmpty(baseline.Executions);
        Assert.NotEmpty(filtered.Executions);
        Assert.True(filteredMetrics.CompletedTrades <= baselineMetrics.CompletedTrades);

        var userOptions = options with { SmaPeriod = 140, AtrPeriod = 5 };
        var userRequest = request with { MaxHoldingDays = userOptions.MaxHoldingDays };
        var userBaseline = new BacktestRunner().Run(userRequest, new UnfilteredSwingAtrStrategy(userOptions));
        var userFiltered = new BacktestRunner().Run(userRequest, new SwingAtrStrategy(userOptions));
        var userBaselineMetrics = BacktestMetricsCalculator.Calculate(startingCash, userBaseline.Executions);
        var userFilteredMetrics = BacktestMetricsCalculator.Calculate(startingCash, userFiltered.Executions);

        WriteComparison("입력값 (SMA 140 / ATR 5)", userBaseline, userBaselineMetrics, userFiltered, userFilteredMetrics);

        Assert.NotEmpty(userBaseline.Executions);
        Assert.NotEmpty(userFiltered.Executions);
        Assert.True(userFilteredMetrics.CompletedTrades <= userBaselineMetrics.CompletedTrades);
    }

    [Fact]
    public void Loader_reads_the_provided_kodex_daily_and_five_minute_files()
    {
        var root = FindRepositoryRoot();
        var samples = Path.Combine(root, "samples", "market-data");
        var loader = new CsvMarketDataLoader();

        var daily = loader.LoadDaily(Path.Combine(samples, "KODEX 200.csv"));
        var intraday = loader.LoadFiveMinute(Path.Combine(samples, "5분봉.csv"));

        Assert.NotEmpty(daily);
        Assert.NotEmpty(intraday);
        Assert.True(daily.Zip(daily.Skip(1)).All(pair => pair.First.Date <= pair.Second.Date));
        Assert.True(intraday.Zip(intraday.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));

        var result = new BacktestRunner().Run(new BacktestRequest(daily, intraday, 10_000_000m, 0.2m), new SwingAtrStrategy(new StrategyOptions()));
        Assert.NotNull(result);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AssetPilot.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException();
    }

    private static void WriteComparison(string label, BacktestResult baseline, BacktestMetrics baselineMetrics, BacktestResult filtered, BacktestMetrics filteredMetrics)
    {
        Console.WriteLine(label);
        Console.WriteLine($"  필터 없음: 거래={baselineMetrics.CompletedTrades}, 수익률={baselineMetrics.ReturnRate:P2}, 손익={baselineMetrics.TotalProfit:N0}");
        Console.WriteLine($"  필터 적용: 거래={filteredMetrics.CompletedTrades}, 수익률={filteredMetrics.ReturnRate:P2}, 손익={filteredMetrics.TotalProfit:N0}");
        Console.WriteLine($"  필터 탈락: {string.Join(", ", filtered.Diagnostics.GroupBy(diagnostic => diagnostic.Code).OrderBy(group => group.Key).Select(group => $"{group.Key}={group.Count()}"))}");
    }

    private sealed class UnfilteredSwingAtrStrategy(StrategyOptions options) : ITradingStrategy
    {
        public string Key => "swing-atr-unfiltered";

        public string DisplayName => "ATR 단일 진입 (필터 없음)";

        public EntryPlan? EvaluateAtClose(IReadOnlyList<DailyBar> dailyBars)
        {
            var requiredBars = Math.Max(options.SmaPeriod, options.AtrPeriod);
            if (dailyBars.Count < requiredBars)
            {
                return null;
            }

            var signalBar = dailyBars[^1];
            var atrBars = dailyBars.TakeLast(options.AtrPeriod).ToArray();
            var atr = atrBars.Select((bar, index) =>
            {
                var previousClose = index == 0 ? bar.Close : atrBars[index - 1].Close;
                return Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
            }).Average();

            return new EntryPlan(
                signalBar.Date,
                signalBar.Close - (atr * options.BuyBandAtrMultiplier),
                signalBar.Close + (atr * options.BuyBandAtrMultiplier),
                atr);
        }

        public ExitPlan CreateExitPlan(decimal entryPrice, decimal atr) => new(
            entryPrice + (atr * options.TargetAtrMultiplier),
            entryPrice - (atr * options.StopAtrMultiplier));
    }
}
