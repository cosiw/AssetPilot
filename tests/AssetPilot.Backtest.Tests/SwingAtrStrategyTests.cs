using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Tests;

public sealed class SwingAtrStrategyTests
{
    [Fact]
    public void EvaluateAtClose_returns_a_buy_band_when_close_is_above_120_day_sma()
    {
        var strategy = new SwingAtrStrategy(new StrategyOptions());

        var plan = strategy.EvaluateAtClose(CreateBars());

        Assert.NotNull(plan);
        Assert.Equal(9_800m, plan.BuyLowerPrice);
        Assert.Equal(10_200m, plan.BuyUpperPrice);
        Assert.Equal(400m, plan.Atr);
    }

    [Fact]
    public void CreateExitPlan_uses_the_actual_entry_price()
    {
        var strategy = new SwingAtrStrategy(new StrategyOptions());

        var exit = strategy.CreateExitPlan(9_900m, 400m);

        Assert.Equal(10_500m, exit.TargetPrice);
        Assert.Equal(9_500m, exit.StopPrice);
    }

    [Fact]
    public void EvaluateAtClose_rejects_a_non_positive_long_sma_slope()
    {
        var strategy = new SwingAtrStrategy(new StrategyOptions
        {
            SmaPeriod = 3, AtrPeriod = 1, ShortSmaPeriod = 2,
            SmaSlopeLookbackDays = 2, MinimumSmaDistancePercent = 0m
        });
        var bars = CreateBars(11m, 11m, 10m, 10m, 11m);

        Assert.Null(strategy.EvaluateAtClose(bars));
        Assert.Equal("LongSmaSlopeNotPositive", strategy.ExplainNoEntry(bars)?.Code);
    }

    [Fact]
    public void EvaluateAtClose_rejects_a_close_that_is_not_one_percent_above_long_sma()
    {
        var strategy = new SwingAtrStrategy(new StrategyOptions
        {
            SmaPeriod = 3, AtrPeriod = 1, ShortSmaPeriod = 2,
            SmaSlopeLookbackDays = 1, MinimumSmaDistancePercent = 1m
        });
        var bars = CreateBars(10m, 10m, 10m, 10.05m);

        Assert.Null(strategy.EvaluateAtClose(bars));
        Assert.Equal("MinimumSmaDistanceNotMet", strategy.ExplainNoEntry(bars)?.Code);
    }

    [Fact]
    public void EvaluateAtClose_rejects_when_short_sma_is_not_above_long_sma()
    {
        var strategy = new SwingAtrStrategy(new StrategyOptions
        {
            SmaPeriod = 4, AtrPeriod = 1, ShortSmaPeriod = 2,
            SmaSlopeLookbackDays = 1, MinimumSmaDistancePercent = 0m
        });
        var bars = CreateBars(8m, 10m, 12m, 10m, 11m);

        Assert.Null(strategy.EvaluateAtClose(bars));
        Assert.Equal("ShortSmaNotAboveLongSma", strategy.ExplainNoEntry(bars)?.Code);
    }

    [Fact]
    public void EvaluateAtClose_returns_a_buy_band_when_all_trend_filters_pass()
    {
        var strategy = new SwingAtrStrategy(new StrategyOptions
        {
            SmaPeriod = 3, AtrPeriod = 1, ShortSmaPeriod = 2,
            SmaSlopeLookbackDays = 2, MinimumSmaDistancePercent = 1m
        });

        Assert.NotNull(strategy.EvaluateAtClose(CreateBars(8m, 9m, 10m, 11m, 12m)));
    }

    private static IReadOnlyList<DailyBar> CreateBars()
    {
        var start = new DateOnly(2026, 1, 1);
        var bars = Enumerable.Range(0, 124)
            .Select(index => new DailyBar(start.AddDays(index), 9_900m, 10_100m, 9_700m, 9_900m))
            .ToList();

        bars.Add(new DailyBar(start.AddDays(124), 10_000m, 10_200m, 9_800m, 10_000m));
        return bars;
    }

    private static IReadOnlyList<DailyBar> CreateBars(params decimal[] closes) => closes
        .Select((close, index) => new DailyBar(new DateOnly(2026, 1, 1).AddDays(index), close, close, close, close))
        .ToArray();
}
