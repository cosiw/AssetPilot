using System.Globalization;
using System.Text;
using System.Text.Json;
using AssetPilot.Backtest.Storage;

namespace AssetPilot.Infrastructure.Backtests;

public sealed class BacktestRunCsvExporter(IBacktestRunStore store) : IBacktestRunExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task<BacktestCsvExport> ExportAsync(Guid backtestId, string outputDirectory, CancellationToken cancellationToken = default)
    {
        var run = await store.GetAsync(backtestId, cancellationToken) ?? throw new KeyNotFoundException($"Backtest run '{backtestId}' was not found.");
        Directory.CreateDirectory(outputDirectory);
        var summaryPath = Path.Combine(outputDirectory, "backtest_summary.csv");
        var executionsPath = Path.Combine(outputDirectory, "backtest_executions.csv");
        var equityPath = Path.Combine(outputDirectory, "backtest_equity.csv");
        var cyclesPath = Path.Combine(outputDirectory, "backtest_position_cycles.csv");
        var exitReasonPerformancePath = Path.Combine(outputDirectory, "backtest_exit_reason_performance.csv");
        var monthlyPerformancePath = Path.Combine(outputDirectory, "backtest_monthly_performance.csv");
        var options = ReadExitOptions(run.StrategyOptionsJson);
        var report = run.PerformanceReport;

        await WriteAsync(summaryPath,
        [
            "BacktestId,StrategyVersion,StartDate,EndDate,TakeProfitAtrMultiplier,StopLossAtrMultiplier,MaxHoldingDays,TotalReturn,CAGR,MDD,WinRate,TradeCount,ProfitFactor,AverageReturn",
            Csv(run.Id, run.StrategyKey, run.DataStartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (run.Executions.Count == 0 ? null : run.Executions.Min(x => x.ExecutedAt).ToString("O")), run.DataEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (run.Executions.Count == 0 ? null : run.Executions.Max(x => x.ExecutedAt).ToString("O")), options.Target, options.Stop, options.MaxHoldingDays, run.ReturnRate, report?.Cagr, run.EquityCurve?.MaxDrawdownRate, report?.WinRate, report?.CompletedTrades ?? run.Executions.Count, report?.ProfitFactor, report?.AverageReturn)
        ], cancellationToken);

        var executionLines = new List<string> { "BacktestId,ExecutionId,ExecutionSequence,Symbol,DateTime,Side,Price,Quantity,GrossAmount,Fee,Tax,RealizedPnL,ExitReason,StrategyVersion" };
        executionLines.AddRange(run.Executions.OrderBy(x => x.Sequence).Select(x => Csv(run.Id, x.ExecutionId, x.Sequence, run.Symbol, x.ExecutedAt.ToString("O"), x.Side, x.Price, x.Quantity, x.GrossAmount, x.Fee, x.Tax, x.RealizedProfit, x.Reason, run.StrategyKey)));
        await WriteAsync(executionsPath, executionLines, cancellationToken);

        var equityLines = new List<string> { "BacktestId,DateTime,Cash,PositionQuantity,AverageEntryPrice,MarkPrice,PositionValue,TotalEquity,CumulativeReturn,PeakEquity,DrawdownAmount,DrawdownRate" };
        equityLines.AddRange((run.EquityCurve?.Points ?? []).OrderBy(x => x.DateTime).Select(x => Csv(run.Id, x.DateTime.ToString("O"), x.Cash, x.PositionQuantity, x.AverageEntryPrice, x.MarkPrice, x.PositionValue, x.TotalEquity, x.CumulativeReturn, x.PeakEquity, x.DrawdownAmount, x.DrawdownRate)));
        await WriteAsync(equityPath, equityLines, cancellationToken);

        var excursionsByCycleId = run.Excursions.ToDictionary(excursion => excursion.CycleId);
        var cycleLines = new List<string> { "BacktestId,CycleId,OpenedAt,ClosedAt,BoughtQuantity,SoldQuantity,AverageEntryPrice,AverageExitPrice,RealizedPnL,ReturnRate,ClosingReason,HoldingMfeRate,HoldingMaeRate,FixedMfeRate,FixedMaeRate,PostExitMfeRate,PostExitMaeRate" };
        cycleLines.AddRange(run.CompletedPositionCyclesOrEmpty.OrderBy(x => x.OpenedAt).Select(cycle =>
        {
            excursionsByCycleId.TryGetValue(cycle.CycleId, out var excursion);
            return Csv(run.Id, cycle.CycleId, cycle.OpenedAt.ToString("O"), cycle.ClosedAt.ToString("O"), cycle.BoughtQuantity, cycle.SoldQuantity, cycle.AverageEntryPrice, cycle.AverageExitPrice, cycle.RealizedProfit, cycle.ReturnRate, cycle.ClosingReason, excursion?.HoldingMfeRate, excursion?.HoldingMaeRate, excursion?.FixedMfeRate, excursion?.FixedMaeRate, excursion?.PostExitMfeRate, excursion?.PostExitMaeRate);
        }));
        await WriteAsync(cyclesPath, cycleLines, cancellationToken);

        var exitReasonLines = new List<string> { "BacktestId,ExitReason,TradeCount,TotalProfit,WinRate,AverageReturn" };
        exitReasonLines.AddRange((report?.ExitReasonPerformances ?? []).Select(performance => Csv(run.Id, performance.Reason?.ToString() ?? "Other", performance.TradeCount, performance.TotalProfit, performance.WinRate, performance.AverageReturn)));
        await WriteAsync(exitReasonPerformancePath, exitReasonLines, cancellationToken);

        var monthlyLines = new List<string> { "BacktestId,Month,TradeCount,TotalProfit,WinRate,AverageReturn" };
        monthlyLines.AddRange((report?.MonthlyPerformances ?? []).Select(performance => Csv(run.Id, performance.Month.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), performance.TradeCount, performance.TotalProfit, performance.WinRate, performance.AverageReturn)));
        await WriteAsync(monthlyPerformancePath, monthlyLines, cancellationToken);

        return new BacktestCsvExport(summaryPath, executionsPath, equityPath, cyclesPath, exitReasonPerformancePath, monthlyPerformancePath);
    }

    private static async Task WriteAsync(string path, IEnumerable<string> lines, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, Utf8Bom);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(line);
        }
    }

    private static string Csv(params object?[] fields) => string.Join(',', fields.Select(Format));
    private static string Format(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    private static ExitOptions ReadExitOptions(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new ExitOptions(
                Decimal(root, "TargetAtrMultiplier"),
                Decimal(root, "StopAtrMultiplier"),
                Integer(root, "MaxHoldingDays"));
        }
        catch (JsonException)
        {
            return new ExitOptions(null, null, null);
        }
    }

    private static decimal? Decimal(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetDecimal(out var result) ? result : null;
    private static int? Integer(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
    private sealed record ExitOptions(decimal? Target, decimal? Stop, int? MaxHoldingDays);
}
