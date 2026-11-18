using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestDiagnosticTests
{
    [Fact]
    public void Run_records_missing_intraday_data_for_a_pending_entry_day()
    {
        var result = new BacktestRunner().Run(new BacktestRequest(
            [new DailyBar(new DateOnly(2026, 1, 1), 1m, 1m, 1m, 1m), new DailyBar(new DateOnly(2026, 1, 2), 1m, 1m, 1m, 1m)], [], 1_000m, 1m), new FixedStrategy());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MissingIntradayData");
    }

    [Fact]
    public void Run_records_buy_band_not_reached_when_next_session_stays_outside_the_entry_band()
    {
        var result = new BacktestRunner().Run(new BacktestRequest(
            [new DailyBar(new DateOnly(2026, 1, 1), 1m, 1m, 1m, 1m), new DailyBar(new DateOnly(2026, 1, 2), 3m, 3m, 3m, 3m)],
            [new FiveMinuteBar(new DateTime(2026, 1, 2, 9, 0, 0), 3m, 3m, 3m, 3m)], 1_000m, 1m), new FixedStrategy());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "BuyBandNotReached" && diagnostic.TradeDate == new DateOnly(2026, 1, 2));
    }

    [Fact]
    public void Run_records_minimum_sma_distance_failure_when_swing_strategy_cannot_create_an_entry_plan()
    {
        var strategy = new SwingAtrStrategy(new StrategyOptions
        {
            SmaPeriod = 2,
            AtrPeriod = 1,
            ShortSmaPeriod = 2,
            SmaSlopeLookbackDays = 1,
            MinimumSmaDistancePercent = 1m
        });
        var result = new BacktestRunner().Run(new BacktestRequest(
            [new DailyBar(new DateOnly(2026, 1, 1), 8m, 8m, 8m, 8m), new DailyBar(new DateOnly(2026, 1, 2), 12m, 12m, 12m, 12m), new DailyBar(new DateOnly(2026, 1, 3), 10m, 10m, 10m, 10m)], [], 1_000m, 1m), strategy);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MinimumSmaDistanceNotMet" && diagnostic.TradeDate == new DateOnly(2026, 1, 3));
    }

    [Fact]
    public void Diagnostic_exposes_a_Korean_display_name_for_the_result_screen()
    {
        Assert.Equal("매수 구간 미도달", new BacktestDiagnostic("BuyBandNotReached", new DateOnly(2026, 1, 2), "").DisplayName);
    }

    [Theory]
    [InlineData("LongSmaSlopeNotPositive", "장기 이평 기울기 미충족")]
    [InlineData("MinimumSmaDistanceNotMet", "최소 SMA 이격도 미충족")]
    [InlineData("ShortSmaNotAboveLongSma", "단기 이평 추세 미충족")]
    public void Diagnostic_exposes_a_Korean_display_name_for_each_trend_filter(string code, string expected)
    {
        Assert.Equal(expected, new BacktestDiagnostic(code, new DateOnly(2026, 1, 2), string.Empty).DisplayName);
    }

    private sealed class FixedStrategy : ITradingStrategy
    {
        public string Key => "fixed"; public string DisplayName => "fixed";
        public EntryPlan? EvaluateAtClose(IReadOnlyList<DailyBar> bars) => new(bars[^1].Date, 1m, 1m, 1m);
        public ExitPlan CreateExitPlan(decimal entryPrice, decimal atr) => new(2m, 0.5m);
    }
}
