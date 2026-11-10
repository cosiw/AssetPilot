using AssetPilot.Core.Transactions;

namespace AssetPilot.Core.Brokerage;

public interface IKiwoomTransactionClient
{
    Task<IReadOnlyList<AccountTransaction>> GetTransactionsAsync(
        string accountNumber,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
