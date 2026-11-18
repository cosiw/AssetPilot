using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Tests;

public sealed class BaselineStrategyOptionsTests
{
    [Fact]
    public void Create_uses_fixed_entry_rules_and_supplied_exit_rules()
    {
        var options = BaselineStrategyOptions.Create(new ExitStrategyOptions(
            TargetAtrMultiplier: 2m,
            StopAtrMultiplier: 0.8m,
            MaxHoldingDays: 7));

        Assert.Equal(120, options.SmaPeriod);
        Assert.Equal(5, options.ShortSmaPeriod);
        Assert.Equal(14, options.AtrPeriod);
        Assert.Equal(5, options.SmaSlopeLookbackDays);
        Assert.Equal(1m, options.MinimumSmaDistancePercent);
        Assert.Equal(0.5m, options.BuyBandAtrMultiplier);
        Assert.Equal(2m, options.TargetAtrMultiplier);
        Assert.Equal(0.8m, options.StopAtrMultiplier);
        Assert.Equal(7, options.MaxHoldingDays);
    }
}
