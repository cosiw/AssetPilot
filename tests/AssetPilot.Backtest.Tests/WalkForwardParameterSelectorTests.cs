using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Tests;

public sealed class WalkForwardParameterSelectorTests
{
    [Fact]
    public void Select_returns_insufficient_history_when_three_years_are_missing()
    {
        var daily = Enumerable.Range(0, 200).Select(index => new DailyBar(new DateOnly(2026, 1, 1).AddDays(index), 100m, 101m, 99m, 100m)).ToArray();

        var result = new WalkForwardParameterSelector().Select(daily, [], new DateOnly(2026, 11, 30));

        Assert.Equal(ParameterDecisionStatus.InsufficientHistory, result.Status);
        Assert.Null(result.Options);
    }
}
