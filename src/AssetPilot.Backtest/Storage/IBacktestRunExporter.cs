namespace AssetPilot.Backtest.Storage;

public interface IBacktestRunExporter
{
    Task<BacktestCsvExport> ExportAsync(Guid backtestId, string outputDirectory, CancellationToken cancellationToken = default);
}

public sealed record BacktestCsvExport(
    string SummaryPath,
    string ExecutionsPath,
    string EquityPath,
    string PositionCyclesPath,
    string ExitReasonPerformancePath,
    string MonthlyPerformancePath);
