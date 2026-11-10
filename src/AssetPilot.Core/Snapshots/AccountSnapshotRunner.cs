using AssetPilot.Core.Accounts;
using AssetPilot.Core.Brokerage;
using AssetPilot.Core.Portfolio;
using PortfolioModel = AssetPilot.Core.Portfolio.Portfolio;

namespace AssetPilot.Core.Snapshots;

public sealed class AccountSnapshotRunner(
    IKiwoomAccountStore accountStore,
    IKiwoomPortfolioClient portfolioClient,
    IKiwoomCashBalanceClient cashBalanceClient,
    IKiwoomTransactionClient transactionClient,
    IAccountSnapshotStore snapshotStore,
    TimeProvider timeProvider)
{
    private static readonly TimeSpan KoreaStandardTimeOffset = TimeSpan.FromHours(9);
    private const string Source = "Kiwoom";

    public async Task<AccountSnapshotRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var capturedAtUtc = timeProvider.GetUtcNow();
        if (!SnapshotSchedule.IsEligible(capturedAtUtc))
        {
            return new AccountSnapshotRunResult(0, 0, 0, 0);
        }

        var snapshotDate = DateOnly.FromDateTime(capturedAtUtc.ToOffset(KoreaStandardTimeOffset).DateTime);
        var accounts = await accountStore.GetAllAsync(cancellationToken);
        var savedSnapshotCount = 0;
        var upsertedTransactionCount = 0;
        var failedAccountCount = 0;

        foreach (var account in accounts)
        {
            try
            {
                if (!await snapshotStore.ExistsAsync(account.AccountId, snapshotDate, cancellationToken))
                {
                    var portfolio = await portfolioClient.GetPortfolioAsync(account.AccountNumber, cancellationToken);
                    var cashBalance = await cashBalanceClient.GetCashBalanceAsync(account.AccountNumber, cancellationToken);
                    await snapshotStore.SaveSnapshotAsync(
                        CreateSnapshot(account, snapshotDate, capturedAtUtc, portfolio, cashBalance),
                        cancellationToken);
                    savedSnapshotCount++;
                }

                var latestTransactionDate = await snapshotStore.GetLatestTransactionDateAsync(account.AccountId, cancellationToken);
                var transactionStartDate = latestTransactionDate ?? snapshotDate;
                var transactions = await transactionClient.GetTransactionsAsync(
                    account.AccountNumber,
                    transactionStartDate,
                    snapshotDate,
                    cancellationToken);
                if (transactions.Count > 0)
                {
                    await snapshotStore.UpsertTransactionsAsync(transactions, cancellationToken);
                    upsertedTransactionCount += transactions.Count;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failedAccountCount++;
            }
        }

        return new AccountSnapshotRunResult(
            accounts.Count,
            savedSnapshotCount,
            upsertedTransactionCount,
            failedAccountCount);
    }

    private static AccountSnapshot CreateSnapshot(
        KiwoomAccountProfile account,
        DateOnly snapshotDate,
        DateTimeOffset capturedAtUtc,
        PortfolioModel portfolio,
        decimal cashBalance) =>
        new(
            account.AccountId,
            snapshotDate,
            capturedAtUtc,
            cashBalance,
            true,
            portfolio.TotalAssetAmount,
            portfolio.TotalPurchaseAmount,
            portfolio.TotalEvaluationAmount,
            portfolio.TotalProfitLoss,
            ToHundredthsOfPercent(portfolio.TotalProfitRate),
            portfolio.Positions.Select(position => new PositionSnapshot(
                string.Empty,
                position.StockCode,
                position.StockName,
                position.Quantity,
                position.AveragePrice,
                position.CurrentPrice,
                position.PurchaseAmount,
                position.EvaluationAmount,
                position.EvaluationProfitLoss,
                ToHundredthsOfPercent(position.ProfitRate))).ToArray(),
            Source);

    private static int ToHundredthsOfPercent(decimal rate) =>
        decimal.ToInt32(decimal.Round(rate * 100, 0, MidpointRounding.AwayFromZero));
}
