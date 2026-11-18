using AssetPilot.Backtest.Execution;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestMetricsTests
{
    [Fact]
    public void Calculate_returns_win_rate_profit_factor_and_max_drawdown()
    {
        var executions = new[]
        {
            new BacktestExecution(new DateTime(2026, 1, 1), ExecutionSide.Sell, 1m, 1m, 100m),
            new BacktestExecution(new DateTime(2026, 1, 2), ExecutionSide.Sell, 1m, 1m, -50m),
        };

        var metrics = BacktestMetricsCalculator.Calculate(1_000m, executions);

        Assert.Equal(2, metrics.CompletedTrades);
        Assert.Equal(0.5m, metrics.WinRate);
        Assert.Equal(2m, metrics.ProfitFactor);
        Assert.Equal(50m, metrics.MaxDrawdown);
        Assert.Equal(100m, metrics.AverageWin);
        Assert.Equal(-50m, metrics.AverageLoss);
        Assert.Equal(1, metrics.MaxConsecutiveLosses);
    }

    [Fact]
    public void Calculate_tracks_the_longest_losing_streak()
    {
        var executions = new[]
        {
            new BacktestExecution(new DateTime(2026, 1, 1), ExecutionSide.Sell, 1m, 1m, -10m),
            new BacktestExecution(new DateTime(2026, 1, 2), ExecutionSide.Sell, 1m, 1m, -20m),
            new BacktestExecution(new DateTime(2026, 1, 3), ExecutionSide.Sell, 1m, 1m, 5m),
        };

        var metrics = BacktestMetricsCalculator.Calculate(1_000m, executions);

        Assert.Equal(2, metrics.MaxConsecutiveLosses);
    }
}
