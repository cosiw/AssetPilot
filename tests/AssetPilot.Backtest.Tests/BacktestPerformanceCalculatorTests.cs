using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestPerformanceCalculatorTests
{
    [Fact]
    public void Calculate_returns_cagr_exit_reason_and_monthly_performance()
    {
        var cycles = new[]
        {
            Cycle("2026-01-10", 100m, 0.10m, ExitReason.Target),
            Cycle("2026-02-10", -50m, -0.05m, ExitReason.Stop),
            Cycle("2026-02-20", 30m, 0.03m, ExitReason.Time),
        };

        var report = BacktestPerformanceCalculator.Calculate(
            1_000m,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 1),
            cycles,
            []);

        Assert.Equal(3, report.CompletedTrades);
        Assert.Equal(80m, report.TotalProfit);
        Assert.Equal(0.08m, report.TotalReturn);
        Assert.True(report.Cagr > 0m);
        Assert.Equal(3, report.ExitReasonPerformances.Count);
        Assert.Equal(2, report.MonthlyPerformances.Count);
    }

    [Fact]
    public void Calculate_uses_other_for_cycles_without_a_closing_reason()
    {
        var report = BacktestPerformanceCalculator.Calculate(
            1_000m,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            [Cycle("2026-01-10", -20m, -0.02m, null)],
            []);

        var other = Assert.Single(report.ExitReasonPerformances);
        Assert.Null(other.Reason);
        Assert.Equal(1, other.TradeCount);
        Assert.Equal(-20m, other.TotalProfit);
    }

    private static BacktestPositionCycle Cycle(string closedAt, decimal profit, decimal returnRate, ExitReason? reason) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.Parse("2026-01-02T09:00:00"),
            DateTime.Parse(closedAt).AddHours(15),
            1m,
            1m,
            1_000m,
            1_000m + profit,
            profit,
            returnRate,
            reason);
}
