namespace AssetPilot.Core.Snapshots;

public sealed record AccountSnapshotRunResult(
    int ProcessedAccountCount,
    int SavedSnapshotCount,
    int UpsertedTransactionCount,
    int FailedAccountCount)
{
    public bool Succeeded => FailedAccountCount == 0;
}
