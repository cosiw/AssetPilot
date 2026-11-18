using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Analysis;

public sealed class BacktestExcursionCalculator
{
    public IReadOnlyList<BacktestExcursion> Calculate(
        IReadOnlyList<FiveMinuteBar> bars,
        IReadOnlyList<BacktestExecution> executions,
        IReadOnlyList<BacktestPositionCycle> cycles,
        BacktestAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(executions);
        ArgumentNullException.ThrowIfNull(cycles);
        ArgumentNullException.ThrowIfNull(options);

        var orderedBars = bars.OrderBy(bar => bar.Timestamp).ToArray();
        var executionsById = executions.ToDictionary(execution => execution.ExecutionId);
        return cycles
            .OrderBy(cycle => cycle.ClosedAt)
            .Select(cycle => CalculateCycle(cycle, orderedBars, executionsById, options))
            .ToArray();
    }

    private static BacktestExcursion CalculateCycle(
        BacktestPositionCycle cycle,
        IReadOnlyList<FiveMinuteBar> bars,
        IReadOnlyDictionary<Guid, BacktestExecution> executionsById,
        BacktestAnalysisOptions options)
    {
        var openingExecution = FindExecution(executionsById, cycle.OpeningExecutionId, "opening");
        var closingExecution = FindExecution(executionsById, cycle.ClosingExecutionId, "closing");
        var entryBarTimestamp = FloorToFiveMinutes(openingExecution.ExecutedAt);
        var closingBarTimestamp = FloorToFiveMinutes(closingExecution.ExecutedAt);
        var holdingBars = bars.Where(bar => bar.Timestamp > entryBarTimestamp && bar.Timestamp < closingBarTimestamp).ToArray();
        var fixedDates = bars
            .Where(bar => bar.Timestamp.Date >= openingExecution.ExecutedAt.Date)
            .Select(bar => DateOnly.FromDateTime(bar.Timestamp))
            .Distinct()
            .Take(options.FixedObservationTradingDays)
            .ToHashSet();
        var fixedBars = bars
            .Where(bar => fixedDates.Contains(DateOnly.FromDateTime(bar.Timestamp)))
            .Where(bar => bar.Timestamp > entryBarTimestamp && bar.Timestamp != closingBarTimestamp)
            .ToArray();
        var postExitBars = fixedBars.Where(bar => bar.Timestamp > closingBarTimestamp).ToArray();

        var holding = CalculateRange(
            cycle.AverageEntryPrice,
            holdingBars.Select(bar => bar.High).Append(openingExecution.Price).Append(closingExecution.Price),
            holdingBars.Select(bar => bar.Low).Append(openingExecution.Price).Append(closingExecution.Price));
        var fixedRange = CalculateRange(
            cycle.AverageEntryPrice,
            fixedBars.Select(bar => bar.High).Append(openingExecution.Price).Append(closingExecution.Price),
            fixedBars.Select(bar => bar.Low).Append(openingExecution.Price).Append(closingExecution.Price));
        (decimal Maximum, decimal Minimum)? postExit = postExitBars.Length == 0
            ? null
            : CalculateRange(closingExecution.Price, postExitBars.Select(bar => bar.High), postExitBars.Select(bar => bar.Low));

        return new BacktestExcursion(
            cycle.CycleId,
            holding.Maximum - cycle.AverageEntryPrice,
            CalculateRate(holding.Maximum - cycle.AverageEntryPrice, cycle.AverageEntryPrice),
            holding.Minimum - cycle.AverageEntryPrice,
            CalculateRate(holding.Minimum - cycle.AverageEntryPrice, cycle.AverageEntryPrice),
            fixedRange.Maximum - cycle.AverageEntryPrice,
            CalculateRate(fixedRange.Maximum - cycle.AverageEntryPrice, cycle.AverageEntryPrice),
            fixedRange.Minimum - cycle.AverageEntryPrice,
            CalculateRate(fixedRange.Minimum - cycle.AverageEntryPrice, cycle.AverageEntryPrice),
            postExit is null ? null : CalculateRate(postExit.Value.Maximum - closingExecution.Price, closingExecution.Price),
            postExit is null ? null : CalculateRate(postExit.Value.Minimum - closingExecution.Price, closingExecution.Price),
            options.FixedObservationTradingDays);
    }

    private static BacktestExecution FindExecution(IReadOnlyDictionary<Guid, BacktestExecution> executionsById, Guid executionId, string description) =>
        executionsById.TryGetValue(executionId, out var execution)
            ? execution
            : throw new InvalidOperationException($"The {description} execution '{executionId}' was not found.");

    private static (decimal Maximum, decimal Minimum) CalculateRange(decimal baseline, IEnumerable<decimal> highs, IEnumerable<decimal> lows) =>
        (highs.DefaultIfEmpty(baseline).Max(), lows.DefaultIfEmpty(baseline).Min());

    private static decimal CalculateRate(decimal amount, decimal baseline) => baseline == 0m ? 0m : amount / baseline;

    private static DateTime FloorToFiveMinutes(DateTime timestamp) =>
        new(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, timestamp.Minute / 5 * 5, 0, timestamp.Kind);
}
