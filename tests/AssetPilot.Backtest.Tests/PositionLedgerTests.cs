using AssetPilot.Backtest.Execution;

namespace AssetPilot.Backtest.Tests;

public sealed class PositionLedgerTests
{
    [Fact]
    public void Apply_records_weighted_average_cost_and_partial_sale_profit()
    {
        var ledger = new PositionLedger(1_000_000m);
        ledger.Apply(Buy(1, "2026-01-02T09:00:00", 10_000m, 100m));
        ledger.Apply(Buy(2, "2026-01-02T10:00:00", 10_200m, 50m));
        var sale = ledger.Apply(Sell(3, "2026-01-02T11:00:00", 10_600m, 80m));

        Assert.Equal(150m, ledger.Snapshots[1].Quantity);
        Assert.Equal(10_066.666666666666666666666667m, ledger.Snapshots[1].AverageEntryPrice);
        Assert.Equal(70m, sale.Snapshot.Quantity);
        Assert.Equal(42_666.6666666666666666666666m, sale.Execution.RealizedProfit);
    }

    [Fact]
    public void Apply_creates_a_cycle_only_when_quantity_returns_to_zero()
    {
        var ledger = new PositionLedger(1_000_000m);
        ledger.Apply(Buy(1, "2026-01-02T09:00:00", 10_000m, 100m));
        ledger.Apply(Buy(2, "2026-01-02T10:00:00", 10_200m, 50m));
        ledger.Apply(Sell(3, "2026-01-02T11:00:00", 10_600m, 80m));
        ledger.Apply(Sell(4, "2026-01-02T12:00:00", 10_400m, 70m, ExitReason.Time));

        var cycle = Assert.Single(ledger.CompletedCycles);
        Assert.Equal(150m, cycle.BoughtQuantity);
        Assert.Equal(150m, cycle.SoldQuantity);
        Assert.Equal(0m, ledger.Snapshots[^1].Quantity);
        Assert.Equal(0m, ledger.Snapshots[^1].AverageEntryPrice);
        Assert.Equal(ExitReason.Time, cycle.ClosingReason);
    }

    [Fact]
    public void Apply_rejects_an_oversell_without_changing_state()
    {
        var ledger = new PositionLedger(1_000_000m);
        ledger.Apply(Buy(1, "2026-01-02T09:00:00", 10_000m, 100m));

        Assert.Throws<InvalidOperationException>(() => ledger.Apply(Sell(2, "2026-01-02T10:00:00", 10_000m, 101m)));
        Assert.Single(ledger.Snapshots);
        Assert.Equal(100m, ledger.Snapshots[0].Quantity);
    }

    private static BacktestExecution Buy(int sequence, string executedAt, decimal price, decimal quantity) =>
        Create(sequence, executedAt, ExecutionSide.Buy, price, quantity, null);

    private static BacktestExecution Sell(int sequence, string executedAt, decimal price, decimal quantity, ExitReason? reason = null) =>
        Create(sequence, executedAt, ExecutionSide.Sell, price, quantity, reason);

    private static BacktestExecution Create(int sequence, string executedAt, ExecutionSide side, decimal price, decimal quantity, ExitReason? reason) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{sequence:D12}"), sequence, DateTime.Parse(executedAt), side, price, quantity, price * quantity, 0m, 0m, null, reason);
}
