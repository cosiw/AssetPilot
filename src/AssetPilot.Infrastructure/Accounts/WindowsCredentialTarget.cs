namespace AssetPilot.Infrastructure.Accounts;

public static class WindowsCredentialTarget
{
    private const string Prefix = "AssetPilot:Kiwoom:";

    public static string Create(string accountNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);

        return Prefix + accountNumber;
    }

    public static bool TryParse(string? target, out string accountNumber)
    {
        accountNumber = string.Empty;

        if (string.IsNullOrWhiteSpace(target) || !target.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        accountNumber = target[Prefix.Length..];
        return !string.IsNullOrWhiteSpace(accountNumber);
    }
}
