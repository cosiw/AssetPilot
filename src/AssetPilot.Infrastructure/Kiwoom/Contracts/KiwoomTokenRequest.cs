using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomTokenRequest(string appKey, string secretKey)
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; } = "client_credentials";

    [JsonPropertyName("appkey")]
    public string AppKey { get; } = appKey;

    [JsonPropertyName("secretkey")]
    public string SecretKey { get; } = secretKey;
}
