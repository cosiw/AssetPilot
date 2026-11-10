using AssetPilot.Core.Accounts;
using AssetPilot.Infrastructure.Accounts;

namespace AssetPilot.Core.Tests;

public sealed class WindowsCredentialFormatTests
{
    [Fact]
    public void Target_round_trips_account_number()
    {
        var target = WindowsCredentialTarget.Create("1234567890");

        Assert.Equal("AssetPilot:Kiwoom:1234567890", target);
        Assert.True(WindowsCredentialTarget.TryParse(target, out var accountNumber));
        Assert.Equal("1234567890", accountNumber);
    }

    [Fact]
    public void Payload_round_trips_secret_key_and_environment()
    {
        var bytes = KiwoomCredentialPayload.Serialize("secret-key", KiwoomEnvironment.Mock);

        var payload = KiwoomCredentialPayload.Deserialize(bytes);

        Assert.Equal("secret-key", payload.SecretKey);
        Assert.Equal(KiwoomEnvironment.Mock, payload.Environment);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"secretKey\":\"\",\"environment\":0}")]
    [InlineData("{\"secretKey\":\"secret-key\",\"environment\":99}")]
    public void Payload_rejects_malformed_or_invalid_values(string json)
    {
        Assert.Throws<InvalidOperationException>(() =>
            KiwoomCredentialPayload.Deserialize(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public async Task SaveAsync_writes_account_to_its_credential_target()
    {
        var credentialManager = new FakeWindowsCredentialManager();
        var store = new WindowsKiwoomAccountStore(credentialManager);
        var account = new KiwoomAccountCredentials(
            new KiwoomAccountProfile("1234567890", KiwoomEnvironment.Mock),
            "app-key",
            "secret-key");

        await store.SaveAsync(account);

        var credential = Assert.Single(credentialManager.Written);
        Assert.Equal("AssetPilot:Kiwoom:1234567890", credential.TargetName);
        Assert.Equal("app-key", credential.UserName);
        var payload = KiwoomCredentialPayload.Deserialize(credential.CredentialBlob);
        Assert.Equal("secret-key", payload.SecretKey);
        Assert.Equal(KiwoomEnvironment.Mock, payload.Environment);
    }

    [Fact]
    public async Task GetAllAsync_ignores_foreign_targets_and_orders_account_numbers()
    {
        var credentialManager = new FakeWindowsCredentialManager
        {
            Credentials =
            [
                new WindowsCredential("OtherApp:Key", "ignored", []),
                CreateCredential("9876543210", KiwoomEnvironment.Real),
                CreateCredential("1234567890", KiwoomEnvironment.Mock)
            ]
        };
        var store = new WindowsKiwoomAccountStore(credentialManager);

        var accounts = await store.GetAllAsync();

        Assert.Collection(
            accounts,
            account => Assert.Equal("1234567890", account.AccountNumber),
            account => Assert.Equal("9876543210", account.AccountNumber));
    }

    [Fact]
    public async Task GetAllAsync_skips_a_corrupted_assetpilot_credential()
    {
        var credentialManager = new FakeWindowsCredentialManager
        {
            Credentials =
            [
                CreateCredential("1234567890", KiwoomEnvironment.Mock),
                new WindowsCredential("AssetPilot:Kiwoom:9876543210", "app-key", "not-json"u8.ToArray())
            ]
        };
        var store = new WindowsKiwoomAccountStore(credentialManager);

        var accounts = await store.GetAllAsync();

        var account = Assert.Single(accounts);
        Assert.Equal("1234567890", account.AccountNumber);
    }

    [Fact]
    public async Task GetAllAsync_backfills_a_stable_account_id_for_a_legacy_credential()
    {
        var credentialManager = new FakeWindowsCredentialManager
        {
            Credentials = [CreateCredential("1234567890", KiwoomEnvironment.Mock)]
        };
        var store = new WindowsKiwoomAccountStore(credentialManager);

        var first = Assert.Single(await store.GetAllAsync());
        var second = Assert.Single(await store.GetAllAsync());

        Assert.NotEqual(Guid.Empty, first.AccountId);
        Assert.Equal(first.AccountId, second.AccountId);
        var migratedPayload = KiwoomCredentialPayload.Deserialize(Assert.Single(credentialManager.Written).CredentialBlob);
        Assert.Equal(first.AccountId, migratedPayload.AccountId);
    }

    [Fact]
    public async Task GetAsync_reconstructs_credentials_for_the_requested_account()
    {
        var credentialManager = new FakeWindowsCredentialManager
        {
            Credentials = [CreateCredential("1234567890", KiwoomEnvironment.Real)]
        };
        var store = new WindowsKiwoomAccountStore(credentialManager);

        var credentials = await store.GetAsync("1234567890");

        Assert.Equal("1234567890", credentials.Profile.AccountNumber);
        Assert.Equal(KiwoomEnvironment.Real, credentials.Profile.Environment);
        Assert.Equal("app-key", credentials.AppKey);
        Assert.Equal("secret-key", credentials.SecretKey);
    }

    [Fact]
    public async Task SaveAsync_rejects_an_empty_app_key()
    {
        var credentialManager = new FakeWindowsCredentialManager();
        var store = new WindowsKiwoomAccountStore(credentialManager);
        var account = new KiwoomAccountCredentials(
            new KiwoomAccountProfile("1234567890", KiwoomEnvironment.Mock),
            " ",
            "secret-key");

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(account));
        Assert.Empty(credentialManager.Written);
    }

    [Fact]
    public async Task DeleteAsync_deletes_only_the_derived_credential_target()
    {
        var credentialManager = new FakeWindowsCredentialManager();
        var store = new WindowsKiwoomAccountStore(credentialManager);

        await store.DeleteAsync("1234567890");

        Assert.Equal(["AssetPilot:Kiwoom:1234567890"], credentialManager.Deleted);
    }

    private static WindowsCredential CreateCredential(string accountNumber, KiwoomEnvironment environment) =>
        new(
            WindowsCredentialTarget.Create(accountNumber),
            "app-key",
            KiwoomCredentialPayload.Serialize("secret-key", environment));

    private sealed class FakeWindowsCredentialManager : IWindowsCredentialManager
    {
        public List<WindowsCredential> Written { get; } = [];
        public List<string> Deleted { get; } = [];
        public IReadOnlyList<WindowsCredential> Credentials { get; init; } = [];

        public WindowsCredential? Read(string targetName) =>
            Credentials.Concat(Written).LastOrDefault(credential => credential.TargetName == targetName);

        public void Write(WindowsCredential credential) => Written.Add(credential);

        public IReadOnlyList<WindowsCredential> Enumerate(string targetFilter) =>
            Credentials
                .Concat(Written)
                .GroupBy(credential => credential.TargetName, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();

        public void Delete(string targetName) => Deleted.Add(targetName);
    }
}
