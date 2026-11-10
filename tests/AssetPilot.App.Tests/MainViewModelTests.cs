using AssetPilot.App.ViewModels;
using AssetPilot.Core.Accounts;
using AssetPilot.Core.Snapshots;

namespace AssetPilot.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task InitializeAsync_with_no_accounts_shows_message_without_reading_snapshots()
    {
        var snapshotReader = new FakeSnapshotReader();
        var viewModel = CreateViewModel(new FakeAccountStore(), snapshotReader);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Null(viewModel.SelectedAccount);
        Assert.NotEmpty(viewModel.StatusMessage);
        Assert.Empty(snapshotReader.RequestedAccounts);
    }

    [Fact]
    public async Task InitializeAsync_selects_the_first_account_by_account_number()
    {
        var accountStore = new FakeAccountStore(
            new KiwoomAccountProfile("9876543210", KiwoomEnvironment.Real),
            new KiwoomAccountProfile("1234567890", KiwoomEnvironment.Mock));
        var viewModel = CreateViewModel(accountStore, new FakeSnapshotReader());

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Collection(
            viewModel.Accounts,
            account => Assert.Equal("1234567890", account.AccountNumber),
            account => Assert.Equal("9876543210", account.AccountNumber));
        Assert.Equal("1234567890", viewModel.SelectedAccount!.AccountNumber);
    }

    [Fact]
    public async Task InitializeAsync_maps_the_latest_saved_snapshot_to_the_portfolio()
    {
        var account = new KiwoomAccountProfile("1234567890", KiwoomEnvironment.Mock);
        var older = CreateDetail(account.AccountId, new DateOnly(2026, 11, 8), 1_000_000m, 100_000m);
        var latest = CreateDetail(account.AccountId, new DateOnly(2026, 11, 9), 1_500_000m, 200_000m);
        var snapshotReader = new FakeSnapshotReader(older, latest);
        var viewModel = CreateViewModel(new FakeAccountStore(account), snapshotReader);

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(1_500_000m, viewModel.Portfolio!.TotalAssetAmount);
        Assert.Equal(200_000m, viewModel.Portfolio.CashBalance);
        Assert.Equal(12.34m, viewModel.Portfolio.TotalProfitRate);
        Assert.Equal(latest.Summary.CapturedAtUtc, viewModel.SnapshotCapturedAtUtc);
        Assert.Equal(new DateOnly(2026, 11, 9), viewModel.AssetHistory.SelectedSnapshotDate);
        Assert.Contains(account.AccountId, snapshotReader.RequestedAccounts);
    }

    [Fact]
    public async Task InitializeAsync_with_an_account_without_snapshots_shows_empty_state()
    {
        var account = new KiwoomAccountProfile("1234567890", KiwoomEnvironment.Mock);
        var viewModel = CreateViewModel(new FakeAccountStore(account), new FakeSnapshotReader());

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Portfolio);
        Assert.NotEmpty(viewModel.StatusMessage);
        Assert.False(viewModel.AssetHistory.HasHistory);
    }

    [Fact]
    public async Task SaveAsync_requires_a_secret_key_without_writing_credentials()
    {
        var accountStore = new MutableAccountStore();
        var viewModel = new AccountManagementViewModel(accountStore)
        {
            AccountNumber = "1234567890",
            Environment = KiwoomEnvironment.Mock,
            AppKey = "app-key"
        };

        await viewModel.SaveCommand.ExecuteAsync(string.Empty);

        Assert.Null(accountStore.SavedAccount);
        Assert.NotEmpty(viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveAsync_writes_credentials_and_reloads_the_account_list()
    {
        var accountStore = new MutableAccountStore();
        var viewModel = new AccountManagementViewModel(accountStore)
        {
            AccountNumber = "1234567890",
            Environment = KiwoomEnvironment.Mock,
            AppKey = "app-key"
        };

        await viewModel.SaveCommand.ExecuteAsync("secret-key");

        Assert.Equal("1234567890", accountStore.SavedAccount!.Profile.AccountNumber);
        Assert.Equal(KiwoomEnvironment.Mock, accountStore.SavedAccount.Profile.Environment);
        Assert.Equal("app-key", accountStore.SavedAccount.AppKey);
        Assert.Equal("secret-key", accountStore.SavedAccount.SecretKey);
        Assert.Single(viewModel.Accounts);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_selected_account()
    {
        var profile = new KiwoomAccountProfile("1234567890", KiwoomEnvironment.Real);
        var accountStore = new MutableAccountStore(profile);
        var viewModel = new AccountManagementViewModel(accountStore);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.SelectedAccount = profile;

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal("1234567890", accountStore.DeletedAccountNumber);
        Assert.Empty(viewModel.Accounts);
    }

    private static MainViewModel CreateViewModel(IKiwoomAccountStore accountStore, FakeSnapshotReader snapshotReader) =>
        new(accountStore, snapshotReader, new AssetHistoryViewModel(snapshotReader));

    private static AccountSnapshotDetail CreateDetail(Guid accountId, DateOnly date, decimal totalAssetAmount, decimal cashBalance) =>
        new(
            new AccountSnapshotSummary(
                date,
                new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                cashBalance,
                true,
                totalAssetAmount,
                1_200_000m,
                1_300_000m,
                100_000m,
                1_234),
            [new PositionSnapshot("KRX", "005930", "Samsung Electronics", 10, 100_000m, 130_000m, 1_000_000m, 1_300_000m, 300_000m, 3_000)]);

    private sealed class FakeAccountStore(params KiwoomAccountProfile[] accounts) : IKiwoomAccountStore
    {
        public Task<IReadOnlyList<KiwoomAccountProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KiwoomAccountProfile>>(accounts);

        public Task<KiwoomAccountCredentials> GetAsync(string accountNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string accountNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSnapshotReader(params AccountSnapshotDetail[] details) : IAccountSnapshotReader
    {
        private readonly IReadOnlyList<AccountSnapshotDetail> _details = details;

        public List<Guid> RequestedAccounts { get; } = [];

        public Task<AccountSnapshotDetail?> GetSnapshotAsync(
            Guid accountId,
            DateOnly snapshotDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_details.SingleOrDefault(detail =>
                detail.Summary.SnapshotDate == snapshotDate &&
                _details.Any(candidate => candidate.Summary.SnapshotDate == snapshotDate)));

        public Task<IReadOnlyList<AccountSnapshotSummary>> GetSummariesAsync(
            Guid accountId,
            DateOnly? from,
            DateOnly? to,
            CancellationToken cancellationToken = default)
        {
            RequestedAccounts.Add(accountId);
            return Task.FromResult<IReadOnlyList<AccountSnapshotSummary>>(_details
                .Select(detail => detail.Summary)
                .Where(summary => (from is null || summary.SnapshotDate >= from) && (to is null || summary.SnapshotDate <= to))
                .OrderBy(summary => summary.SnapshotDate)
                .ToArray());
        }
    }

    private sealed class MutableAccountStore(params KiwoomAccountProfile[] accounts) : IKiwoomAccountStore
    {
        private readonly List<KiwoomAccountProfile> _accounts = [.. accounts];

        public KiwoomAccountCredentials? SavedAccount { get; private set; }
        public string? DeletedAccountNumber { get; private set; }

        public Task<IReadOnlyList<KiwoomAccountProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KiwoomAccountProfile>>(_accounts);

        public Task<KiwoomAccountCredentials> GetAsync(string accountNumber, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken = default)
        {
            SavedAccount = account;
            _accounts.RemoveAll(profile => profile.AccountNumber == account.Profile.AccountNumber);
            _accounts.Add(account.Profile);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string accountNumber, CancellationToken cancellationToken = default)
        {
            DeletedAccountNumber = accountNumber;
            _accounts.RemoveAll(profile => profile.AccountNumber == accountNumber);
            return Task.CompletedTask;
        }
    }
}
