using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomCashBalanceRequest
{
    [JsonPropertyName("qry_tp")]
    public string QueryType { get; init; } = "3";
}
