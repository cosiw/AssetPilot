using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomConsignedTransactionResponse
{
    [JsonPropertyName("trst_ovrl_trde_prps_array")] public KiwoomConsignedTransactionItem[] Items { get; init; } = [];
}

internal sealed class KiwoomConsignedTransactionItem
{
    [JsonPropertyName("trde_dt")] public string? Date { get; init; }
    [JsonPropertyName("trde_no")] public string? Number { get; init; }
    [JsonPropertyName("rmrk_nm")] public string? Remark { get; init; }
    [JsonPropertyName("stk_cd")] public string? StockCode { get; init; }
    [JsonPropertyName("stk_nm")] public string? StockName { get; init; }
    [JsonPropertyName("trde_qty_jwa_cnt")] public string? Quantity { get; init; }
    [JsonPropertyName("trde_unit")] public string? UnitPrice { get; init; }
    [JsonPropertyName("exct_amt")] public string? SettlementAmount { get; init; }
    [JsonPropertyName("cmsn")] public string? Fee { get; init; }
    [JsonPropertyName("trde_agri_tax")] public string? Tax { get; init; }
}
