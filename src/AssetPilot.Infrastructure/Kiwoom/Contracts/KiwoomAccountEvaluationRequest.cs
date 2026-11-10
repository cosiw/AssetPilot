using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomAccountEvaluationRequest
{
    [JsonPropertyName("qry_tp")]
    public string QueryType { get; } = "2";

    [JsonPropertyName("dmst_stex_tp")]
    public string DomesticStockExchangeType { get; } = "KRX";
}
