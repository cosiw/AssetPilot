using System.Text.Json.Serialization;

namespace AssetPilot.Infrastructure.Kiwoom.Contracts;

internal sealed class KiwoomConsignedTransactionRequest(DateOnly from, DateOnly to)
{
    [JsonPropertyName("strt_dt")] public string StartDate { get; } = from.ToString("yyyyMMdd");
    [JsonPropertyName("end_dt")] public string EndDate { get; } = to.ToString("yyyyMMdd");
    [JsonPropertyName("tp")] public string Type { get; } = "0";
    [JsonPropertyName("gds_tp")] public string ProductType { get; } = "1";
    [JsonPropertyName("dmst_stex_tp")] public string ExchangeType { get; } = "KRX";
    [JsonPropertyName("qry_sort_tp")] public string SortType { get; } = "2";
}
