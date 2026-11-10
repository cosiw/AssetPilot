namespace AssetPilot.Core.Accounts;

public enum KiwoomEnvironment
{
    Real,
    Mock
}

public sealed record KiwoomAccountProfile(Guid AccountId, string AccountNumber, KiwoomEnvironment Environment)
{
    public KiwoomAccountProfile(string accountNumber, KiwoomEnvironment environment)
        : this(Guid.NewGuid(), accountNumber, environment)
    {
    }
}
