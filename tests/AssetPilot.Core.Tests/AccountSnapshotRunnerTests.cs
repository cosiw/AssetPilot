using AssetPilot.Core.Accounts;
using AssetPilot.Core.Brokerage;
using AssetPilot.Core.Portfolio;
using AssetPilot.Core.Snapshots;
using AssetPilot.Core.Transactions;
using PortfolioModel = AssetPilot.Core.Portfolio.Portfolio;

namespace AssetPilot.Core.Tests;

public sealed class AccountSnapshotRunnerTests
{
    [Fact]
    public async Task RunAsync_skips_existing_snapshot_but_still_upserts_transactions()
    {
        var account = new KiwoomAccountProfile(Guid.NewGuid(), "1234567890", KiwoomEnvironment.Mock);
        var store = new FakeSnapshotStore { SnapshotExists = true, Transactions = [Dividend(account.AccountId)] };
        var portfolioClient = new FakePortfolioClient();
        var cashBalanceClient = new FakeCashBalanceClient();
        var runner = CreateRunner([account], store, portfolioClient, cashBalanceClient);

        var result = await runner.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, portfolioClient.CallCount);
        Assert.Equal(0, cashBalanceClient.CallCount);
        Assert.Single(store.UpsertedTransactions);
    }

    [Fact]
    public async Task RunAsync_does_nothing_outside_the_eligible_time()
    {
        var account = new KiwoomAccountProfile(Guid.NewGuid(), "1234567890", KiwoomEnvironment.Mock);
        var store = new FakeSnapshotStore();
        var portfolioClient = new FakePortfolioClient();
        var cashBalanceClient = new FakeCashBalanceClient();
        var runner = CreateRunner([account], store, portfolioClient, cashBalanceClient, "2026-11-09T06:59:00+00:00");

        var result = await runner.RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, portfolioClient.CallCount);
        Assert.Equal(0, cashBalanceClient.CallCount);
        Assert.Empty(store.UpsertedTransactions);
    }

    [Fact]
    public async Task RunAsync_saves_today_snapshot_when_none_exists()
    {
        var account = new KiwoomAccountProfile(Guid.NewGuid(), "1234567890", KiwoomEnvironment.Mock);
        var store = new FakeSnapshotStore();
        var runner = CreateRunner([account], store, new FakePortfolioClient(), new FakeCashBalanceClient());

        var result = await runner.RunAsync();

        Assert.True(result.Succeeded);
        var snapshot = Assert.Single(store.SavedSnapshots);
        Assert.Equal(account.AccountId, snapshot.AccountId);
        Assert.Equal(new DateOnly(2026, 11, 9), snapshot.SnapshotDate);
        Assert.IsType<decimal>(snapshot.CashBalance);
        Assert.IsType<decimal>(snapshot.TotalAssetAmount);
        Assert.Equal(1_000_000m, snapshot.CashBalance);
        Assert.True(snapshot.HasCashBalance);
        Assert.Equal(1_500_000m, snapshot.TotalAssetAmount);
    }

    [Fact]
    public async Task RunAsync_continues_with_remaining_accounts_after_an_account_fails()
    {
        var failedAccount = new KiwoomAccountProfile(Guid.NewGuid(), "1111111111", KiwoomEnvironment.Mock);
        var succeedingAccount = new KiwoomAccountProfile(Guid.NewGuid(), "2222222222", KiwoomEnvironment.Mock);
        var store = new FakeSnapshotStore();
        var portfolioClient = new FakePortfolioClient("1111111111");
        var runner = CreateRunner([failedAccount, succeedingAccount], store, portfolioClient, new FakeCashBalanceClient());

        var result = await runner.RunAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.FailedAccountCount);
        Assert.Single(store.SavedSnapshots);
        Assert.Equal(succeedingAccount.AccountId, store.SavedSnapshots[0].AccountId);
    }

    private static AccountSnapshotRunner CreateRunner(
        IReadOnlyList<KiwoomAccountProfile> accounts,
        FakeSnapshotStore store,
        FakePortfolioClient portfolioClient,
        FakeCashBalanceClient cashBalanceClient,
        string utcNow = "2026-11-09T08:00:00+00:00") =>
        new(
            new FakeAccountStore(accounts),
            portfolioClient,
            cashBalanceClient,
            new FakeTransactionClient(store.Transactions),
            store,
            new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));

    private static AccountTransaction Dividend(Guid accountId) =>
        new(
            accountId,
            "dividend-1",
            new DateOnly(2026, 11, 9),
            null,
            TransactionType.Dividend,
            "Dividend",
            null,
            null,
            null,
            null,
            10_000,
            0,
            0,
            DateTimeOffset.Parse("2026-11-09T08:00:00+00:00"),
            "Kiwoom");

    private sealed class FakeAccountStore(IReadOnlyList<KiwoomAccountProfile> accounts) : IKiwoomAccountStore
    {
        public Task<IReadOnlyList<KiwoomAccountProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts);

        public Task<KiwoomAccountCredentials> GetAsync(string accountNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string accountNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePortfolioClient(string? failingAccountNumber = null) : IKiwoomPortfolioClient
    {
        public int CallCount { get; private set; }

        public Task<PortfolioModel> GetPortfolioAsync(string accountNumber, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (accountNumber == failingAccountNumber)
            {
                throw new InvalidOperationException("Portfolio lookup failed.");
            }

            return Task.FromResult(new PortfolioModel(400_000, 500_000, 100_000, 25, [], TotalAssetAmount: 1_500_000));
        }
    }

    private sealed class FakeCashBalanceClient(decimal cashBalance = 1_000_000) : IKiwoomCashBalanceClient
    {
        public int CallCount { get; private set; }

        public Task<decimal> GetCashBalanceAsync(string accountNumber, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(cashBalance);
        }
    }

    private sealed class FakeTransactionClient(IReadOnlyList<AccountTransaction> transactions) : IKiwoomTransactionClient
    {
        public Task<IReadOnlyList<AccountTransaction>> GetTransactionsAsync(
            string accountNumber,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) => Task.FromResult(transactions);
    }

    private sealed class FakeSnapshotStore : IAccountSnapshotStore
    {
        public bool SnapshotExists { get; init; }
        public List<AccountSnapshot> SavedSnapshots { get; } = [];
        public List<AccountTransaction> UpsertedTransactions { get; } = [];
        public IReadOnlyList<AccountTransaction> Transactions { get; init; } = [];

        public Task<bool> ExistsAsync(Guid accountId, DateOnly snapshotDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(SnapshotExists);

        public Task SaveSnapshotAsync(AccountSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SavedSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<DateOnly?> GetLatestTransactionDateAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult<DateOnly?>(null);

        public Task UpsertTransactionsAsync(IReadOnlyList<AccountTransaction> transactions, CancellationToken cancellationToken = default)
        {
            UpsertedTransactions.AddRange(transactions);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
