namespace AssetPilot.Backtest.Storage;

public interface IBacktestRunStore
{
    Task SaveAsync(BacktestRun run, CancellationToken cancellationToken = default);
    Task<BacktestRun?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
