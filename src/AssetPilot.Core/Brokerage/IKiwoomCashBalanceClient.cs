namespace AssetPilot.Core.Brokerage;

public interface IKiwoomCashBalanceClient
{
    Task<decimal> GetCashBalanceAsync(string accountNumber, CancellationToken cancellationToken = default);
}
