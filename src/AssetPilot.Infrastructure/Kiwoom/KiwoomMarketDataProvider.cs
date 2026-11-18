using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AssetPilot.Backtest.MarketData;
using AssetPilot.Core.Accounts;
using AssetPilot.Infrastructure.Kiwoom.Contracts;

namespace AssetPilot.Infrastructure.Kiwoom;

public sealed class KiwoomMarketDataProvider(HttpClient httpClient, IKiwoomAccountStore accountStore) : IMarketDataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string Source => "kiwoom";

    public async Task<IReadOnlyList<DailyMarketBar>> GetDailyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        using var document = await GetChartAsync("ka10081", new { stk_cd = symbol, base_dt = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture), upd_stkpc_tp = "1" }, cancellationToken);
        return FindItems(document.RootElement).Select(item => new DailyMarketBar(Date(item, "stk_dt", "dt"), Number(item, "open_pric", "open"), Number(item, "high_pric", "high"), Number(item, "low_pric", "low"), Number(item, "cur_prc", "close"), Volume(item))).Where(bar => bar.Date >= from && bar.Date <= to).OrderBy(bar => bar.Date).ToArray();
    }

    public async Task<IReadOnlyList<IntradayMarketBar>> GetFiveMinuteAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        using var document = await GetChartAsync("ka11080", new { stk_cd = symbol, tic_scope = "5", base_dt = to.ToString("yyyyMMdd", CultureInfo.InvariantCulture), upd_stkpc_tp = "1" }, cancellationToken);
        return FindItems(document.RootElement).Select(item => new IntradayMarketBar(DateTime(item), Number(item, "open_pric", "open"), Number(item, "high_pric", "high"), Number(item, "low_pric", "low"), Number(item, "cur_prc", "close"), Volume(item))).Where(bar => DateOnly.FromDateTime(bar.Timestamp) >= from && DateOnly.FromDateTime(bar.Timestamp) <= to).OrderBy(bar => bar.Timestamp).ToArray();
    }

    private async Task<JsonDocument> GetChartAsync(string apiId, object payload, CancellationToken cancellationToken)
    {
        var profile = (await accountStore.GetAllAsync(cancellationToken)).FirstOrDefault(profile => profile.Environment == KiwoomEnvironment.Real) ?? throw new InvalidOperationException("A real Kiwoom account is required for market data.");
        var credentials = await accountStore.GetAsync(profile.AccountNumber, cancellationToken);
        using var tokenResponse = await httpClient.PostAsJsonAsync("https://api.kiwoom.com/oauth2/token", new KiwoomTokenRequest(credentials.AppKey, credentials.SecretKey), JsonOptions, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<KiwoomTokenResponse>(JsonOptions, cancellationToken) ?? throw new InvalidOperationException("Kiwoom token response body was empty.");
        if (token.ReturnCode != 0 || string.IsNullOrWhiteSpace(token.Token)) throw new InvalidOperationException("Kiwoom access-token issuance failed.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.kiwoom.com/api/dostk/chart") { Content = JsonContent.Create(payload, options: JsonOptions) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Add("api-id", apiId);
        request.Headers.Add("cont-yn", "N");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
    }

    private static IEnumerable<JsonElement> FindItems(JsonElement root)
    {
        foreach (var property in root.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.Array) return property.Value.EnumerateArray().ToArray();
        return [];
    }

    private static DateOnly Date(JsonElement item, params string[] names) => DateOnly.ParseExact(Text(item, names), "yyyyMMdd", CultureInfo.InvariantCulture);
    private static DateTime DateTime(JsonElement item) => System.DateTime.ParseExact($"{Text(item, "stk_dt", "dt")} {Text(item, "cntr_tm", "time")}", "yyyyMMdd HHmmss", CultureInfo.InvariantCulture);
    private static decimal Number(JsonElement item, params string[] names) => Math.Abs(decimal.Parse(Text(item, names), NumberStyles.Number, CultureInfo.InvariantCulture));
    private static long Volume(JsonElement item) => Math.Abs(long.Parse(Text(item, "trde_qty", "volume"), NumberStyles.Integer, CultureInfo.InvariantCulture));
    private static string Text(JsonElement item, params string[] names) { foreach (var name in names) if (item.TryGetProperty(name, out var value)) return value.GetString() ?? "0"; throw new FormatException($"Kiwoom chart field is missing: {string.Join('/', names)}"); }
}
