using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestExcursionCalculatorTests
{
    [Fact]
    public void Calculate_excludes_entry_and_exit_bars_but_includes_actual_fill_prices()
    {
        var openingId = Guid.NewGuid();
        var closingId = Guid.NewGuid();
        var cycle = Cycle(openingId, closingId, 100m, "2026-01-02T09:00:00", "2026-01-02T09:10:00");
        var executions = new[]
        {
            Execution(openingId, 1, "2026-01-02T09:00:00", ExecutionSide.Buy, 100m),
            Execution(closingId, 2, "2026-01-02T09:10:00", ExecutionSide.Sell, 105m),
        };
        var bars = new[]
        {
            Bar("2026-01-02T09:00:00", high: 130m, low: 70m),
            Bar("2026-01-02T09:05:00", high: 110m, low: 90m),
            Bar("2026-01-02T09:10:00", high: 140m, low: 60m),
        };

        var result = Assert.Single(new BacktestExcursionCalculator().Calculate(
            bars,
            executions,
            [cycle],
            new BacktestAnalysisOptions(1)));

        Assert.Equal(10m, result.HoldingMfeAmount);
        Assert.Equal(-10m, result.HoldingMaeAmount);
        Assert.Equal(0.10m, result.HoldingMfeRate);
        Assert.Equal(-0.10m, result.HoldingMaeRate);
    }

    [Fact]
    public void Calculate_observes_prices_after_exit_until_the_fixed_horizon()
    {
        var openingId = Guid.NewGuid();
        var closingId = Guid.NewGuid();
        var cycle = Cycle(openingId, closingId, 100m, "2026-01-02T09:00:00", "2026-01-02T09:05:00");
        var executions = new[]
        {
            Execution(openingId, 1, "2026-01-02T09:00:00", ExecutionSide.Buy, 100m),
            Execution(closingId, 2, "2026-01-02T09:05:00", ExecutionSide.Sell, 105m),
        };
        var bars = new[]
        {
            Bar("2026-01-02T09:00:00", high: 130m, low: 70m),
            Bar("2026-01-02T09:05:00", high: 140m, low: 60m),
            Bar("2026-01-02T09:10:00", high: 110m, low: 95m),
            Bar("2026-01-05T09:00:00", high: 120m, low: 90m),
            Bar("2026-01-06T09:00:00", high: 200m, low: 50m),
        };

        var result = Assert.Single(new BacktestExcursionCalculator().Calculate(
            bars,
            executions,
            [cycle],
            new BacktestAnalysisOptions(2)));

        Assert.Equal(20m, result.FixedMfeAmount);
        Assert.Equal(-10m, result.FixedMaeAmount);
        Assert.Equal(15m / 105m, result.PostExitMfeRate);
        Assert.Equal(-15m / 105m, result.PostExitMaeRate);
    }

    private static BacktestPositionCycle Cycle(Guid openingId, Guid closingId, decimal entryPrice, string openedAt, string closedAt) =>
        new(Guid.NewGuid(), openingId, closingId, DateTime.Parse(openedAt), DateTime.Parse(closedAt), 1m, 1m, entryPrice, 105m, 5m, 0.05m, ExitReason.Target);

    private static BacktestExecution Execution(Guid id, int sequence, string at, ExecutionSide side, decimal price) =>
        new(id, sequence, DateTime.Parse(at), side, price, 1m, price, 0m, 0m, side == ExecutionSide.Sell ? 0m : null, null);

    private static FiveMinuteBar Bar(string timestamp, decimal high, decimal low) =>
        new(DateTime.Parse(timestamp), 100m, high, low, 100m);
}
