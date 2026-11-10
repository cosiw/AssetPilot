namespace AssetPilot.Core.Accounts;

public sealed record KiwoomAccountCredentials(
    KiwoomAccountProfile Profile,
    string AppKey,
    string SecretKey);
