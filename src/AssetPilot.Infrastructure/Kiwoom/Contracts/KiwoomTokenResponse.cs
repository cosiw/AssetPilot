using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomTokenResponse
{
    [JsonPropertyName("token")]
    public string? Token { get; init; }

    [JsonPropertyName("expires_dt")]
    public string? ExpiresAt { get; init; }

    [JsonPropertyName("return_code")]
    public int ReturnCode { get; init; }
}
