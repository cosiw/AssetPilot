using System.Globalization;
using System.Text;
using AssetPilot.Backtest.Experiments;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Infrastructure.Backtests;

public sealed class BacktestExperimentCsvExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public async Task<string> ExportAsync(
        BacktestExperimentReport experiment,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "backtest_parameter_experiment_summary.csv");
        var lines = new List<string>
        {
            "ExperimentId,BacktestId,StrategyVersion,StartDate,EndDate,TakeProfitAtrMultiplier,StopLossAtrMultiplier,MaxHoldingDays,TotalReturn,CAGR,MDD,WinRate,TradeCount,ProfitFactor,AverageReturn,AverageWin,AverageLoss,MaxConsecutiveLosses,Error"
        };
        lines.AddRange(experiment.Results.Select(result => Csv(
            experiment.ExperimentId,
            result.BacktestId,
            BaselineStrategyOptions.StrategyVersion,
            result.EquityCurve?.Points.MinBy(point => point.DateTime)?.DateTime.ToString("O"),
            result.EquityCurve?.Points.MaxBy(point => point.DateTime)?.DateTime.ToString("O"),
            result.Candidate.TakeProfitAtrMultiplier,
            result.Candidate.StopLossAtrMultiplier,
            result.Candidate.MaxHoldingDays,
            result.PerformanceReport?.TotalReturn,
            result.PerformanceReport?.Cagr,
            result.EquityCurve?.MaxDrawdownRate,
            result.PerformanceReport?.WinRate ?? result.Metrics?.WinRate,
            result.PerformanceReport?.CompletedTrades ?? result.Metrics?.CompletedTrades,
            result.PerformanceReport?.ProfitFactor ?? result.Metrics?.ProfitFactor,
            result.PerformanceReport?.AverageReturn,
            result.PerformanceReport?.AverageWin ?? result.Metrics?.AverageWin,
            result.PerformanceReport?.AverageLoss ?? result.Metrics?.AverageLoss,
            result.PerformanceReport?.MaxConsecutiveLosses ?? result.Metrics?.MaxConsecutiveLosses,
            result.Error)));

        await using var writer = new StreamWriter(path, false, Utf8Bom);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(line);
        }

        return path;
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
}
