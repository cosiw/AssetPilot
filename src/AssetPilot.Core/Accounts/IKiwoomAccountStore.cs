namespace AssetPilot.Core.Accounts;

public interface IKiwoomAccountStore
{
    Task<IReadOnlyList<KiwoomAccountProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<KiwoomAccountCredentials> GetAsync(string accountNumber, CancellationToken cancellationToken = default);
    Task SaveAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken = default);
    Task DeleteAsync(string accountNumber, CancellationToken cancellationToken = default);
}
