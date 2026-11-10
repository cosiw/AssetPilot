namespace AssetPilot.Core.Brokerage;

using AssetPilot.Core.Portfolio;

public interface IKiwoomPortfolioClient
{
    Task<Portfolio> GetPortfolioAsync(string accountNumber, CancellationToken cancellationToken = default);
}
