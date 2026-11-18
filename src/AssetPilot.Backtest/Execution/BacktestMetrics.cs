namespace AssetPilot.Backtest.Execution;

public sealed record BacktestMetrics(
    int CompletedTrades,
    decimal WinRate,
    decimal ProfitFactor,
    decimal MaxDrawdown,
    decimal TotalProfit,
    decimal ReturnRate,
    decimal AverageWin,
    decimal AverageLoss,
    int MaxConsecutiveLosses);

public static class BacktestMetricsCalculator
{
    public static BacktestMetrics Calculate(decimal startingCash, IReadOnlyList<BacktestExecution> executions)
    {
        var profits = executions.Where(execution => execution.Side == ExecutionSide.Sell && execution.RealizedProfit.HasValue).Select(execution => execution.RealizedProfit!.Value).ToArray();
        var winningTrades = profits.Where(profit => profit > 0m).ToArray();
        var losingTrades = profits.Where(profit => profit < 0m).ToArray();
        var grossProfit = winningTrades.Sum();
        var grossLoss = -losingTrades.Sum();
        var equity = startingCash;
        var peak = equity;
        var maxDrawdown = 0m;
        foreach (var profit in profits) { equity += profit; peak = Math.Max(peak, equity); maxDrawdown = Math.Max(maxDrawdown, peak - equity); }
        var consecutiveLosses = 0;
        var maxConsecutiveLosses = 0;
        foreach (var profit in profits)
        {
            consecutiveLosses = profit < 0m ? consecutiveLosses + 1 : 0;
            maxConsecutiveLosses = Math.Max(maxConsecutiveLosses, consecutiveLosses);
        }
        return new BacktestMetrics(
            profits.Length,
            profits.Length == 0 ? 0m : (decimal)winningTrades.Length / profits.Length,
            grossLoss == 0m ? (grossProfit > 0m ? decimal.MaxValue : 0m) : grossProfit / grossLoss,
            maxDrawdown,
            profits.Sum(),
            startingCash == 0m ? 0m : profits.Sum() / startingCash,
            winningTrades.Length == 0 ? 0m : grossProfit / winningTrades.Length,
            losingTrades.Length == 0 ? 0m : losingTrades.Average(),
            maxConsecutiveLosses);
    }
}
