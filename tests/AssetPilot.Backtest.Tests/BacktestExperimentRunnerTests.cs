using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Experiments;
using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestExperimentRunnerTests
{
    [Fact]
    public void Run_reuses_the_fixed_baseline_entry_options_for_every_candidate()
    {
        var report = new BacktestExperimentRunner().Run(
            CreateRequest(),
            ExitParameterExperimentScope.TakeProfitOnly);

        Assert.Equal(3, report.Results.Count);
        Assert.All(report.Results, result =>
        {
            Assert.Equal(120, result.StrategyOptions.SmaPeriod);
            Assert.Equal(5, result.StrategyOptions.ShortSmaPeriod);
            Assert.Equal(14, result.StrategyOptions.AtrPeriod);
            Assert.Equal(5, result.StrategyOptions.SmaSlopeLookbackDays);
            Assert.Equal(1m, result.StrategyOptions.MinimumSmaDistancePercent);
            Assert.Equal(0.5m, result.StrategyOptions.BuyBandAtrMultiplier);
        });
    }

    private static BacktestRequest CreateRequest()
    {
        var start = new DateOnly(2026, 1, 1);
        var dailyBars = Enumerable.Range(0, 126)
            .Select(index =>
            {
                var price = 10_000m + index;
                return new DailyBar(start.AddDays(index), price, price + 10m, price - 10m, price);
            })
            .ToArray();

        return new BacktestRequest(
            dailyBars,
            [new FiveMinuteBar(start.ToDateTime(new TimeOnly(9, 0)), 10_000m, 10_010m, 9_990m, 10_000m)],
            10_000_000m,
            0.2m);
    }
}
