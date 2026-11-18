using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Tests;

public sealed class BacktestEquityCurveCalculatorTests
{
    [Fact]
    public void Calculate_marks_an_open_position_at_each_bar_close_and_finds_drawdown()
    {
        var result = new BacktestEquityCurveCalculator().Calculate(
            1_000m,
            [Buy(1, "2026-01-02T09:00:00", 100m, 5m)],
            [Bar("2026-01-02T09:00:00", 100m), Bar("2026-01-02T09:05:00", 80m)]);

        Assert.Equal(2, result.Points.Count);
        Assert.Equal(900m, result.Points[1].TotalEquity);
        Assert.Equal(100m, result.MaxDrawdownAmount);
        Assert.Equal(0.10m, result.MaxDrawdownRate);
    }

    private static BacktestExecution Buy(int sequence, string executedAt, decimal price, decimal quantity) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{sequence:D12}"), sequence, DateTime.Parse(executedAt), ExecutionSide.Buy, price, quantity, price * quantity, 0m, 0m);

    private static FiveMinuteBar Bar(string timestamp, decimal close) =>
        new(DateTime.Parse(timestamp), close, close, close, close);
}
