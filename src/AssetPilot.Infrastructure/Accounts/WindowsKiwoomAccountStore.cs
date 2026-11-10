using AssetPilot.Core.Accounts;

namespace AssetPilot.Infrastructure.Accounts;

public sealed class WindowsKiwoomAccountStore(IWindowsCredentialManager credentialManager) : IKiwoomAccountStore
{
    public Task<IReadOnlyList<KiwoomAccountProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<KiwoomAccountProfile> accounts = credentialManager
            .Enumerate("AssetPilot:Kiwoom:*")
            .Select(CreateProfileAndMigrateLegacyCredential)
            .OfType<KiwoomAccountProfile>()
            .OrderBy(account => account.AccountNumber, StringComparer.Ordinal)
            .ToArray();

        return Task.FromResult(accounts);
    }

    public Task<KiwoomAccountCredentials> GetAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var credential = credentialManager.Read(WindowsCredentialTarget.Create(accountNumber))
            ?? throw new InvalidOperationException($"Kiwoom credential for account {accountNumber} was not found.");
        if (string.IsNullOrWhiteSpace(credential.UserName))
        {
            throw new InvalidOperationException($"Kiwoom credential for account {accountNumber} is invalid.");
        }

        var payload = KiwoomCredentialPayload.Deserialize(credential.CredentialBlob);
        var accountId = GetOrCreateAccountId(credential, payload);
        var profile = new KiwoomAccountProfile(accountId, accountNumber, payload.Environment);

        return Task.FromResult(new KiwoomAccountCredentials(profile, credential.UserName, payload.SecretKey));
    }

    public Task SaveAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(account.AppKey);

        credentialManager.Write(new WindowsCredential(
            WindowsCredentialTarget.Create(account.Profile.AccountNumber),
            account.AppKey,
            KiwoomCredentialPayload.Serialize(account.SecretKey, account.Profile.Environment, account.Profile.AccountId)));

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        credentialManager.Delete(WindowsCredentialTarget.Create(accountNumber));
        return Task.CompletedTask;
    }

    private KiwoomAccountProfile? CreateProfileAndMigrateLegacyCredential(WindowsCredential credential)
    {
        if (!WindowsCredentialTarget.TryParse(credential.TargetName, out var accountNumber)
            || string.IsNullOrWhiteSpace(credential.UserName))
        {
            return null;
        }

        try
        {
            var payload = KiwoomCredentialPayload.Deserialize(credential.CredentialBlob);
            var accountId = GetOrCreateAccountId(credential, payload);
            return new KiwoomAccountProfile(accountId, accountNumber, payload.Environment);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private Guid GetOrCreateAccountId(WindowsCredential credential, KiwoomCredentialPayload payload)
    {
        if (payload.AccountId is { } accountId && accountId != Guid.Empty)
        {
            return accountId;
        }

        var generatedAccountId = Guid.NewGuid();
        credentialManager.Write(credential with
        {
            CredentialBlob = KiwoomCredentialPayload.Serialize(
                payload.SecretKey,
                payload.Environment,
                generatedAccountId)
        });
        return generatedAccountId;
    }
}
