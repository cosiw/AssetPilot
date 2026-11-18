using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Execution;

public sealed class BacktestRunner
{
    public BacktestResult Run(BacktestRequest request, ITradingStrategy strategy)
    {
        var executions = new List<BacktestExecution>();
        var diagnostics = new List<BacktestDiagnostic>();
        var orderedDailyBars = request.DailyBars.OrderBy(bar => bar.Date).ToArray();
        var intradayByDate = request.FiveMinuteBars.OrderBy(bar => bar.Timestamp).GroupBy(bar => DateOnly.FromDateTime(bar.Timestamp)).ToDictionary(group => group.Key, group => group.ToArray());
        var cash = request.StartingCash;
        PendingEntry? pending = null;
        Position? position = null;

        foreach (var dailyBar in orderedDailyBars)
        {
            if (position is not null && intradayByDate.TryGetValue(dailyBar.Date, out var openPositionBars))
            {
                position = position with { HoldingDays = position.HoldingDays + 1 };
                foreach (var bar in openPositionBars)
                {
                    var sale = TryFillExit(bar, position.ExitPlan);
                    if (sale is null) continue;

                    var profit = (sale.Value.Price - position.EntryPrice) * position.Quantity;
                    cash += sale.Value.Price * position.Quantity;
                    executions.Add(new BacktestExecution(bar.Timestamp, ExecutionSide.Sell, sale.Value.Price, position.Quantity, profit, sale.Value.Reason));
                    position = null;
                    break;
                }

                if (position is not null && request.MaxHoldingDays > 0 && position.HoldingDays >= request.MaxHoldingDays)
                {
                    var finalBar = openPositionBars[^1];
                    var profit = (finalBar.Close - position.EntryPrice) * position.Quantity;
                    cash += finalBar.Close * position.Quantity;
                    executions.Add(new BacktestExecution(finalBar.Timestamp, ExecutionSide.Sell, finalBar.Close, position.Quantity, profit, ExitReason.Time));
                    position = null;
                }
            }

            if (position is null && pending is not null && !intradayByDate.TryGetValue(dailyBar.Date, out _))
            {
                diagnostics.Add(new BacktestDiagnostic("MissingIntradayData", dailyBar.Date, "예약 매수일의 5분봉 데이터가 없습니다."));
                pending = null;
            }
            else if (position is null && pending is not null && intradayByDate.TryGetValue(dailyBar.Date, out var bars))
            {
                var entryFilled = false;
                for (var index = 0; index < bars.Length && position is null; index++)
                {
                    var entry = TryFillEntry(bars[index], pending);
                    if (entry is null)
                    {
                        continue;
                    }

                    entryFilled = true;
                    var quantity = Math.Floor(cash * request.AllocationRatio / entry.Value.Price);
                    if (quantity <= 0)
                    {
                        break;
                    }

                    var exit = strategy.CreateExitPlan(entry.Value.Price, pending.Plan.Atr);
                    cash -= entry.Value.Price * quantity;
                    position = new Position(entry.Value.Price, quantity, exit, 1);
                    executions.Add(new BacktestExecution(bars[index].Timestamp, ExecutionSide.Buy, entry.Value.Price, quantity));

                    var exitStart = entry.Value.AtOpen ? index : index + 1;
                    for (var exitIndex = exitStart; exitIndex < bars.Length && position is not null; exitIndex++)
                    {
                        var sale = TryFillExit(bars[exitIndex], position.ExitPlan);
                        if (sale is null) continue;
                        var profit = (sale.Value.Price - position.EntryPrice) * position.Quantity;
                        cash += sale.Value.Price * position.Quantity;
                        executions.Add(new BacktestExecution(bars[exitIndex].Timestamp, ExecutionSide.Sell, sale.Value.Price, position.Quantity, profit, sale.Value.Reason));
                        position = null;
                    }

                    if (position is not null && request.MaxHoldingDays == 1)
                    {
                        var finalBar = bars[^1];
                        var profit = (finalBar.Close - position.EntryPrice) * position.Quantity;
                        cash += finalBar.Close * position.Quantity;
                        executions.Add(new BacktestExecution(finalBar.Timestamp, ExecutionSide.Sell, finalBar.Close, position.Quantity, profit, ExitReason.Time));
                        position = null;
                    }

                    break;
                }

                if (!entryFilled)
                {
                    diagnostics.Add(new BacktestDiagnostic(
                        "BuyBandNotReached",
                        dailyBar.Date,
                        $"매수 구간 {pending.Plan.BuyLowerPrice:N0}~{pending.Plan.BuyUpperPrice:N0}원을 장중에 터치하지 않았습니다."));
                }
            }

            var history = orderedDailyBars.Where(bar => bar.Date <= dailyBar.Date).ToArray();
            var plan = position is null ? strategy.EvaluateAtClose(history) : null;
            if (position is null && plan is null && strategy is IEntrySignalDiagnosticSource diagnosticSource && diagnosticSource.ExplainNoEntry(history) is { } rejection)
            {
                diagnostics.Add(new BacktestDiagnostic(rejection.Code, dailyBar.Date, rejection.Message));
            }

            pending = plan is { } entryPlan ? new PendingEntry(entryPlan) : null;
        }

        var ledger = new PositionLedger(request.StartingCash);
        var appliedExecutions = executions
            .Select((execution, index) => execution with
            {
                ExecutionId = Guid.NewGuid(),
                Sequence = index + 1,
                GrossAmount = execution.Price * execution.Quantity,
                Fee = 0m,
                Tax = 0m,
                RealizedProfit = null
            })
            .Select(ledger.Apply)
            .ToArray();
        var appliedExecutionList = appliedExecutions.Select(application => application.Execution).ToArray();

        var completedCycles = ledger.CompletedCycles;
        var analysisOptions = request.AnalysisOptions ?? new BacktestAnalysisOptions();
        var excursions = new BacktestExcursionCalculator().Calculate(
            request.FiveMinuteBars,
            appliedExecutionList,
            completedCycles,
            analysisOptions);
        var equityCurve = new BacktestEquityCurveCalculator().Calculate(
            request.StartingCash,
            appliedExecutionList,
            request.FiveMinuteBars);
        var performanceReport = orderedDailyBars.Length == 0
            ? null
            : BacktestPerformanceCalculator.Calculate(
                request.StartingCash,
                orderedDailyBars[0].Date,
                orderedDailyBars[^1].Date,
                completedCycles,
                excursions);

        return new BacktestResult(
            appliedExecutionList,
            diagnostics,
            ledger.Snapshots,
            completedCycles,
            excursions,
            performanceReport,
            equityCurve);
    }

    private static (decimal Price, bool AtOpen)? TryFillEntry(FiveMinuteBar bar, PendingEntry pending)
    {
        if (bar.Open >= pending.Plan.BuyLowerPrice && bar.Open <= pending.Plan.BuyUpperPrice) return (bar.Open, true);
        if (bar.Open < pending.Plan.BuyLowerPrice && bar.High >= pending.Plan.BuyLowerPrice) return (pending.Plan.BuyLowerPrice, false);
        if (bar.Open > pending.Plan.BuyUpperPrice && bar.Low <= pending.Plan.BuyUpperPrice) return (pending.Plan.BuyUpperPrice, false);
        return null;
    }

    private static (decimal Price, ExitReason Reason)? TryFillExit(FiveMinuteBar bar, ExitPlan plan)
    {
        if (bar.Open <= plan.StopPrice) return (bar.Open, ExitReason.Stop);
        if (bar.Open >= plan.TargetPrice) return (plan.TargetPrice, ExitReason.Target);
        if (bar.Low <= plan.StopPrice) return (plan.StopPrice, ExitReason.Stop);
        if (bar.High >= plan.TargetPrice) return (plan.TargetPrice, ExitReason.Target);
        return null;
    }

    private sealed record PendingEntry(EntryPlan Plan);
    private sealed record Position(decimal EntryPrice, decimal Quantity, ExitPlan ExitPlan, int HoldingDays);
}
