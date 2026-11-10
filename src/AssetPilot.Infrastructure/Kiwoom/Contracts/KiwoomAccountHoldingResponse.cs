using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomAccountHoldingResponse
{
    [JsonPropertyName("stk_cd")]
    public string StockCode { get; init; } = string.Empty;

    [JsonPropertyName("stk_nm")]
    public string StockName { get; init; } = string.Empty;

    [JsonPropertyName("rmnd_qty")]
    public string? RemainingQuantity { get; init; }

    [JsonPropertyName("pur_pric")]
    public string? AveragePurchasePrice { get; init; }

    [JsonPropertyName("cur_prc")]
    public string? CurrentPrice { get; init; }

    [JsonPropertyName("pur_amt")]
    public string? PurchaseAmount { get; init; }

    [JsonPropertyName("evlt_amt")]
    public string? EvaluationAmount { get; init; }

    [JsonPropertyName("evltv_prft")]
    public string? EvaluationProfitLoss { get; init; }

    [JsonPropertyName("prft_rt")]
    public string? ProfitRate { get; init; }
}
