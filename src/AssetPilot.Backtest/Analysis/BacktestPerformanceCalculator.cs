using AssetPilot.Backtest.Execution;

namespace AssetPilot.Backtest.Analysis;

public static class BacktestPerformanceCalculator
{
    public static BacktestPerformanceReport Calculate(
        decimal startingCash,
        DateOnly dataStartDate,
        DateOnly dataEndDate,
        IReadOnlyList<BacktestPositionCycle> cycles,
        IReadOnlyList<BacktestExcursion> excursions)
    {
        var completedCycles = cycles.OrderBy(cycle => cycle.ClosedAt).ToArray();
        var profits = completedCycles.Select(cycle => cycle.RealizedProfit).ToArray();
        var winners = profits.Where(profit => profit > 0m).ToArray();
        var losers = profits.Where(profit => profit < 0m).ToArray();
        var totalProfit = profits.Sum();
        var totalReturn = startingCash == 0m ? 0m : totalProfit / startingCash;

        return new BacktestPerformanceReport(
            completedCycles.Length,
            totalProfit,
            totalReturn,
            CalculateCagr(startingCash, totalProfit, dataStartDate, dataEndDate),
            completedCycles.Length == 0 ? 0m : (decimal)winners.Length / completedCycles.Length,
            CalculateProfitFactor(winners, losers),
            completedCycles.Length == 0 ? 0m : completedCycles.Average(cycle => cycle.ReturnRate),
            winners.Length == 0 ? 0m : winners.Average(),
            losers.Length == 0 ? 0m : losers.Average(),
            CalculateMaxConsecutiveLosses(profits),
            CalculateExitReasonPerformances(completedCycles),
            CalculateMonthlyPerformances(completedCycles),
            excursions);
    }

    private static decimal CalculateCagr(decimal startingCash, decimal totalProfit, DateOnly dataStartDate, DateOnly dataEndDate)
    {
        var days = dataEndDate.DayNumber - dataStartDate.DayNumber;
        var endingEquity = startingCash + totalProfit;
        if (startingCash <= 0m || endingEquity <= 0m || days <= 0)
        {
            return 0m;
        }

        return (decimal)Math.Pow((double)(endingEquity / startingCash), 365.25d / days) - 1m;
    }

    private static decimal CalculateProfitFactor(IReadOnlyList<decimal> winners, IReadOnlyList<decimal> losers)
    {
        var grossProfit = winners.Sum();
        var grossLoss = -losers.Sum();
        return grossLoss == 0m ? (grossProfit > 0m ? decimal.MaxValue : 0m) : grossProfit / grossLoss;
    }

    private static int CalculateMaxConsecutiveLosses(IEnumerable<decimal> profits)
    {
        var current = 0;
        var maximum = 0;
        foreach (var profit in profits)
        {
            current = profit < 0m ? current + 1 : 0;
            maximum = Math.Max(maximum, current);
        }

        return maximum;
    }

    private static IReadOnlyList<ExitReasonPerformance> CalculateExitReasonPerformances(IEnumerable<BacktestPositionCycle> cycles) =>
        cycles
            .GroupBy(cycle => cycle.ClosingReason)
            .OrderBy(group => group.Key.HasValue ? (int)group.Key.Value + 1 : 0)
            .Select(group => new ExitReasonPerformance(
                group.Key,
                group.Count(),
                group.Sum(cycle => cycle.RealizedProfit),
                (decimal)group.Count(cycle => cycle.RealizedProfit > 0m) / group.Count(),
                group.Average(cycle => cycle.ReturnRate)))
            .ToArray();

    private static IReadOnlyList<MonthlyPerformance> CalculateMonthlyPerformances(IEnumerable<BacktestPositionCycle> cycles) =>
        cycles
            .GroupBy(cycle => new DateOnly(cycle.ClosedAt.Year, cycle.ClosedAt.Month, 1))
            .OrderBy(group => group.Key)
            .Select(group => new MonthlyPerformance(
                group.Key,
                group.Count(),
                group.Sum(cycle => cycle.RealizedProfit),
                (decimal)group.Count(cycle => cycle.RealizedProfit > 0m) / group.Count(),
                group.Average(cycle => cycle.ReturnRate)))
            .ToArray();
}
