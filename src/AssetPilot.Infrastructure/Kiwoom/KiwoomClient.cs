using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AssetPilot.Core.Accounts;
using AssetPilot.Core.Brokerage;
using AssetPilot.Core.Common;
using AssetPilot.Core.Portfolio;
using AssetPilot.Core.Transactions;
using AssetPilot.Infrastructure.Kiwoom.Contracts;

namespace AssetPilot.Infrastructure.Kiwoom;

public sealed class KiwoomClient(
    HttpClient httpClient,
    IKiwoomAccountStore accountStore,
    TimeProvider timeProvider) : IKiwoomPortfolioClient, IKiwoomCashBalanceClient, IKiwoomTransactionClient
{
    private static readonly Uri RealBaseAddress = new("https://api.kiwoom.com");
    private static readonly Uri MockBaseAddress = new("https://mockapi.kiwoom.com");
    private static readonly TimeSpan RefreshBefore = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KoreaStandardTimeOffset = TimeSpan.FromHours(9);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<TokenCacheKey, CachedToken> _tokens = [];
    private readonly ConcurrentDictionary<TokenCacheKey, SemaphoreSlim> _tokenLocks = [];

    public async Task<Portfolio> GetPortfolioAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        var account = await accountStore.GetAsync(accountNumber, cancellationToken);
        var payload = await PostAsync<KiwoomAccountEvaluationRequest, KiwoomAccountEvaluationResponse>(
            account,
            "kt00018",
            "/api/dostk/acnt",
            new KiwoomAccountEvaluationRequest(),
            cancellationToken);
        return new Portfolio(
            NumericParser.ToDecimal(payload.TotalPurchaseAmount),
            NumericParser.ToDecimal(payload.TotalEvaluationAmount),
            NumericParser.ToDecimal(payload.TotalProfitLoss),
            NumericParser.ToDecimal(payload.TotalProfitRate),
            payload.Holdings.Select(holding => new PortfolioPosition(
                holding.StockCode.TrimStart('A'),
                holding.StockName,
                NumericParser.ToInt32(holding.RemainingQuantity),
                NumericParser.ToDecimal(holding.AveragePurchasePrice),
                NumericParser.ToDecimal(holding.CurrentPrice),
                NumericParser.ToDecimal(holding.PurchaseAmount),
                NumericParser.ToDecimal(holding.EvaluationAmount),
                NumericParser.ToDecimal(holding.EvaluationProfitLoss),
                NumericParser.ToDecimal(holding.ProfitRate))).ToArray(),
            TotalAssetAmount: NumericParser.ToDecimal(payload.TotalAssetAmount));
    }

    public async Task<decimal> GetCashBalanceAsync(string accountNumber, CancellationToken cancellationToken = default)
    {
        var account = await accountStore.GetAsync(accountNumber, cancellationToken);
        var payload = await PostAsync<KiwoomCashBalanceRequest, KiwoomCashBalanceResponse>(
            account,
            "kt00001",
            "/api/dostk/acnt",
            new KiwoomCashBalanceRequest(),
            cancellationToken);

        return NumericParser.ToDecimal(payload.DepositAmount);
    }

    public async Task<IReadOnlyList<AccountTransaction>> GetTransactionsAsync(string accountNumber, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var account = await accountStore.GetAsync(accountNumber, cancellationToken);
        var capturedAtUtc = timeProvider.GetUtcNow();
        var items = new List<KiwoomConsignedTransactionItem>();
        string? nextKey = null;
        var hasMore = true;
        while (hasMore)
        {
            var token = await GetAccessTokenAsync(account, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, GetEndpoint(account.Profile.Environment, "/api/dostk/acnt"))
            {
                Content = CreateJsonContent(new KiwoomConsignedTransactionRequest(from, to))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("api-id", "kt00015");
            request.Headers.Add("cont-yn", nextKey is null ? "N" : "Y");
            if (nextKey is not null) request.Headers.Add("next-key", nextKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<KiwoomConsignedTransactionResponse>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Kiwoom API response body was empty.");
            items.AddRange(payload.Items);
            hasMore = response.Headers.TryGetValues("cont-yn", out var values) && values.SingleOrDefault() == "Y";
            nextKey = hasMore && response.Headers.TryGetValues("next-key", out var keys) ? keys.SingleOrDefault() : null;
        }
        return items.Select(item => new AccountTransaction(
            account.Profile.AccountId,
            item.Number ?? $"{item.Date}:{item.Remark}:{item.StockCode}:{item.SettlementAmount}",
            DateOnly.ParseExact(item.Date!, "yyyyMMdd"), null, MapType(item.Remark), item.Remark ?? string.Empty,
            item.StockCode?.TrimStart('A'), item.StockName, ParseNullableLong(item.Quantity), ParseNullableDecimal(item.UnitPrice),
            NumericParser.ToDecimal(item.SettlementAmount), NumericParser.ToDecimal(item.Fee), NumericParser.ToDecimal(item.Tax), capturedAtUtc, "Kiwoom")).ToArray();
    }

    private static TransactionType MapType(string? remark) => remark switch
    {
        var value when value?.Contains("입금", StringComparison.Ordinal) == true => TransactionType.Deposit,
        var value when value?.Contains("출금", StringComparison.Ordinal) == true => TransactionType.Withdrawal,
        var value when value?.Contains("매수", StringComparison.Ordinal) == true => TransactionType.Buy,
        var value when value?.Contains("매도", StringComparison.Ordinal) == true => TransactionType.Sell,
        var value when value?.Contains("배당", StringComparison.Ordinal) == true => TransactionType.Dividend,
        _ => TransactionType.Other
    };

    private static long? ParseNullableLong(string? value) => string.IsNullOrWhiteSpace(value) ? null : NumericParser.ToInt64(value);

    private static decimal? ParseNullableDecimal(string? value) => string.IsNullOrWhiteSpace(value) ? null : NumericParser.ToDecimal(value);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        KiwoomAccountCredentials account,
        string apiId,
        string path,
        TRequest body,
        CancellationToken cancellationToken)
    {
        var cacheKey = new TokenCacheKey(account.Profile.AccountNumber, account.Profile.Environment);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetAccessTokenAsync(account, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, GetEndpoint(account.Profile.Environment, path))
            {
                Content = CreateJsonContent(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("api-id", apiId);
            request.Headers.Add("cont-yn", "N");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokens.TryRemove(cacheKey, out _);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Kiwoom API response body was empty.");
        }

        throw new InvalidOperationException("Kiwoom API request could not be completed.");
    }

    private async Task<string> GetAccessTokenAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken)
    {
        var cacheKey = new TokenCacheKey(account.Profile.AccountNumber, account.Profile.Environment);
        if (TryGetValidToken(cacheKey, out var token))
        {
            return token;
        }

        var tokenLock = _tokenLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetValidToken(cacheKey, out token))
            {
                return token;
            }

            var cachedToken = await IssueTokenAsync(account, cancellationToken);
            _tokens[cacheKey] = cachedToken;
            return cachedToken.Value;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    private bool TryGetValidToken(TokenCacheKey cacheKey, out string token)
    {
        if (_tokens.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt - RefreshBefore > timeProvider.GetUtcNow())
        {
            token = cached.Value;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private async Task<CachedToken> IssueTokenAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetEndpoint(account.Profile.Environment, "/oauth2/token"))
        {
            Content = CreateJsonContent(new KiwoomTokenRequest(account.AppKey, account.SecretKey))
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<KiwoomTokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Kiwoom token response body was empty.");

        if (payload.ReturnCode != 0 || string.IsNullOrWhiteSpace(payload.Token) || !TryParseExpiration(payload.ExpiresAt, out var expiresAt))
        {
            throw new InvalidOperationException("Kiwoom access-token issuance failed.");
        }

        return new CachedToken(payload.Token, expiresAt);
    }

    private static Uri GetEndpoint(KiwoomEnvironment environment, string path) =>
        new(environment == KiwoomEnvironment.Real ? RealBaseAddress : MockBaseAddress, path);

    private static StringContent CreateJsonContent<T>(T body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    private static bool TryParseExpiration(string? value, out DateTimeOffset expiresAt)
    {
        if (DateTime.TryParseExact(value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localExpiration))
        {
            expiresAt = new DateTimeOffset(localExpiration, KoreaStandardTimeOffset);
            return true;
        }

        expiresAt = default;
        return false;
    }

    private sealed record TokenCacheKey(string AccountNumber, KiwoomEnvironment Environment);
    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);
}
