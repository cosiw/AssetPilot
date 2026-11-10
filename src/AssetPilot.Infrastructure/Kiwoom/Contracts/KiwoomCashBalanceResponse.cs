using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomCashBalanceResponse
{
    [JsonPropertyName("entr")]
    public string? DepositAmount { get; init; }
}
