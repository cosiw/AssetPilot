namespace AssetPilot.Infrastructure.Accounts;

public sealed record WindowsCredential(string TargetName, string UserName, byte[] CredentialBlob);
