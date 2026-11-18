using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Analysis;

public sealed class BacktestEquityCurveCalculator
{
    public BacktestEquityCurve Calculate(
        decimal startingCash,
        IReadOnlyList<BacktestExecution> executions,
        IReadOnlyList<FiveMinuteBar> bars)
    {
        if (startingCash < 0m) throw new ArgumentOutOfRangeException(nameof(startingCash));

        var points = new List<BacktestEquityPoint>();
        var orderedExecutions = executions
            .Select((execution, index) => (execution, index))
            .OrderBy(item => item.execution.ExecutedAt)
            .ThenBy(item => item.execution.Sequence)
            .ThenBy(item => item.index)
            .ToArray();
        var cash = startingCash;
        var quantity = 0m;
        var averageEntryPrice = 0m;
        var positionCost = 0m;
        var executionIndex = 0;
        var peakEquity = startingCash;
        DateTime? peakAt = null;
        DateTime? troughAt = null;
        var maxDrawdownAmount = 0m;
        var maxDrawdownRate = 0m;

        foreach (var bar in bars.OrderBy(bar => bar.Timestamp))
        {
            while (executionIndex < orderedExecutions.Length && orderedExecutions[executionIndex].execution.ExecutedAt <= bar.Timestamp)
            {
                Apply(orderedExecutions[executionIndex].execution, ref cash, ref quantity, ref averageEntryPrice, ref positionCost);
                executionIndex++;
            }

            var positionValue = quantity * bar.Close;
            var totalEquity = cash + positionValue;
            if (totalEquity > peakEquity)
            {
                peakEquity = totalEquity;
                peakAt = bar.Timestamp;
            }

            var drawdownAmount = totalEquity - peakEquity;
            var drawdownRate = peakEquity == 0m ? 0m : drawdownAmount / peakEquity;
            if (-drawdownAmount > maxDrawdownAmount)
            {
                maxDrawdownAmount = -drawdownAmount;
                maxDrawdownRate = -drawdownRate;
                troughAt = bar.Timestamp;
            }

            points.Add(new BacktestEquityPoint(
                bar.Timestamp,
                cash,
                quantity,
                averageEntryPrice,
                bar.Close,
                positionValue,
                totalEquity,
                startingCash == 0m ? 0m : totalEquity / startingCash - 1m,
                peakEquity,
                drawdownAmount,
                drawdownRate));
        }

        return new BacktestEquityCurve(points, maxDrawdownAmount, maxDrawdownRate, peakAt, troughAt);
    }

    private static void Apply(
        BacktestExecution execution,
        ref decimal cash,
        ref decimal quantity,
        ref decimal averageEntryPrice,
        ref decimal positionCost)
    {
        if (execution.Side == ExecutionSide.Buy)
        {
            var totalCost = execution.GrossAmount + execution.Fee + execution.Tax;
            cash -= totalCost;
            positionCost += totalCost;
            quantity += execution.Quantity;
            averageEntryPrice = positionCost / quantity;
            return;
        }

        if (execution.Quantity > quantity)
            throw new InvalidOperationException("Cannot sell more than the current position quantity.");

        cash += execution.GrossAmount - execution.Fee - execution.Tax;
        positionCost -= averageEntryPrice * execution.Quantity;
        quantity -= execution.Quantity;
        if (quantity == 0m)
        {
            positionCost = 0m;
            averageEntryPrice = 0m;
        }
    }
}
