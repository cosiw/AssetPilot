namespace AssetPilot.Core.Snapshots;

public interface IAccountSnapshotReader
{
    Task<AccountSnapshotDetail?> GetSnapshotAsync(
        Guid accountId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountSnapshotSummary>> GetSummariesAsync(
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default);
}
