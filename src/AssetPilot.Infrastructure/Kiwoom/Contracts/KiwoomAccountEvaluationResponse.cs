using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomAccountEvaluationResponse
{
    [JsonPropertyName("tot_pur_amt")]
    public string? TotalPurchaseAmount { get; init; }

    [JsonPropertyName("tot_evlt_amt")]
    public string? TotalEvaluationAmount { get; init; }

    [JsonPropertyName("tot_evlt_pl")]
    public string? TotalProfitLoss { get; init; }

    [JsonPropertyName("tot_prft_rt")]
    public string? TotalProfitRate { get; init; }

    [JsonPropertyName("prsm_dpst_aset_amt")]
    public string? TotalAssetAmount { get; init; }

    [JsonPropertyName("acnt_evlt_remn_indv_tot")]
    public KiwoomAccountHoldingResponse[] Holdings { get; init; } = [];
}
