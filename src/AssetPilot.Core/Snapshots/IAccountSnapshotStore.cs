using AssetPilot.Core.Transactions;

namespace AssetPilot.Core.Snapshots;

public interface IAccountSnapshotStore
{
    Task<bool> ExistsAsync(
        Guid accountId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken = default);

    Task SaveSnapshotAsync(AccountSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<DateOnly?> GetLatestTransactionDateAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);

    Task UpsertTransactionsAsync(
        IReadOnlyList<AccountTransaction> transactions,
        CancellationToken cancellationToken = default);
}
