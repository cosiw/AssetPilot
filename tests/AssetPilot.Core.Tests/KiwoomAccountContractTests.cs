using AssetPilot.Core.Accounts;

namespace AssetPilot.Core.Tests;

public sealed class KiwoomAccountContractTests
{
    [Fact]
    public void Credentials_expose_profile_and_secret_values()
    {
        var profile = new KiwoomAccountProfile("1234567890", KiwoomEnvironment.Mock);
        var credentials = new KiwoomAccountCredentials(profile, "app-key", "secret-key");

        Assert.Equal("1234567890", credentials.Profile.AccountNumber);
        Assert.Equal(KiwoomEnvironment.Mock, credentials.Profile.Environment);
        Assert.Equal("app-key", credentials.AppKey);
        Assert.Equal("secret-key", credentials.SecretKey);
    }
}
