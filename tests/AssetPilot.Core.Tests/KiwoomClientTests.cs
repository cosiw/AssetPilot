using System.Collections.Concurrent;
using System.Net;
using AssetPilot.Core.Accounts;
using AssetPilot.Infrastructure.Kiwoom;

namespace AssetPilot.Core.Tests;

public sealed class KiwoomClientTests
{
    [Fact]
    public async Task GetPortfolioAsync_requests_token_then_maps_account_evaluation_to_portfolio()
    {
        var handler = StubHttpMessageHandler.FromResponses(
            TokenResponse("access-token", "20271206120000"),
            PortfolioResponse(
                """[{"stk_cd":"A005930","stk_nm":"Samsung Electronics","rmnd_qty":"42","pur_pric":"68400","cur_prc":"75800","pur_amt":"2872800","evlt_amt":"3183600","evltv_prft":"310800","prft_rt":"10.82"}]""",
                "2872800",
                "3183600",
                "310800",
                "10.82"));
        using var httpClient = new HttpClient(handler);
        var client = new KiwoomClient(
            httpClient,
            new FixedKiwoomAccountStore("1234567890", "app-key", "secret-key"),
            TimeProvider.System);

        var portfolio = await client.GetPortfolioAsync("1234567890");

        Assert.Equal(2872800m, portfolio.TotalPurchaseAmount);
        Assert.Equal(3183600m, portfolio.TotalEvaluationAmount);
        Assert.Equal(310800m, portfolio.TotalProfitLoss);
        Assert.Equal(10.82m, portfolio.TotalProfitRate);

        var position = Assert.Single(portfolio.Positions);
        Assert.Equal("005930", position.StockCode);
        Assert.Equal("Samsung Electronics", position.StockName);
        Assert.Equal(42, position.Quantity);
        Assert.Equal(68400m, position.AveragePrice);
        Assert.Equal(75800m, position.CurrentPrice);
        Assert.Equal(2872800m, position.PurchaseAmount);
        Assert.Equal(3183600m, position.EvaluationAmount);
        Assert.Equal(310800m, position.EvaluationProfitLoss);
        Assert.Equal(10.82m, position.ProfitRate);

        var requests = handler.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal("/oauth2/token", requests[0].Path);
        Assert.Equal("/api/dostk/acnt", requests[1].Path);
        Assert.Equal("Bearer access-token", requests[1].Authorization);
        Assert.Equal("kt00018", requests[1].ApiId);
    }

    [Fact]
    public async Task GetCashBalanceAsync_requests_token_then_maps_deposit_amount()
    {
        var handler = StubHttpMessageHandler.FromResponses(
            TokenResponse("access-token", "20271206120000"),
            CashBalanceResponse("525000"));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var cashBalance = await client.GetCashBalanceAsync("1234567890");

        Assert.Equal(525000m, cashBalance);
        var requests = handler.Requests.ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal("/oauth2/token", requests[0].Path);
        Assert.Equal("/api/dostk/acnt", requests[1].Path);
        Assert.Equal("Bearer access-token", requests[1].Authorization);
        Assert.Equal("kt00001", requests[1].ApiId);
        Assert.Equal("{\"qry_tp\":\"3\"}", requests[1].Body);
    }

    [Fact]
    public async Task GetPortfolioAsync_reuses_a_valid_token_for_the_same_account()
    {
        var handler = StubHttpMessageHandler.FromResponses(
            TokenResponse("access-token", "20271206130000"),
            PortfolioResponse(),
            CashBalanceResponse(),
            PortfolioResponse(),
            CashBalanceResponse());
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.GetPortfolioAsync("1234567890");
        await client.GetCashBalanceAsync("1234567890");
        await client.GetPortfolioAsync("1234567890");
        await client.GetCashBalanceAsync("1234567890");

        Assert.Equal(1, handler.Requests.Count(request => request.Path == "/oauth2/token"));
        Assert.Equal(4, handler.Requests.Count(request => request.Path == "/api/dostk/acnt"));
        Assert.Equal(2, handler.Requests.Count(request => request.ApiId == "kt00018"));
        Assert.Equal(2, handler.Requests.Count(request => request.ApiId == "kt00001"));
    }

    [Fact]
    public async Task GetPortfolioAsync_refreshes_a_token_inside_the_five_minute_window()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 54, 0, TimeSpan.FromHours(9)));
        var handler = StubHttpMessageHandler.FromResponses(
            TokenResponse("token-1", "20261108130000"),
            PortfolioResponse(),
            TokenResponse("token-2", "20261108131000"),
            PortfolioResponse());
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, timeProvider);

        await client.GetPortfolioAsync("1234567890");
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await client.GetPortfolioAsync("1234567890");

        Assert.Equal(2, handler.Requests.Count(request => request.Path == "/oauth2/token"));
    }

    [Fact]
    public async Task GetPortfolioAsync_issues_one_token_when_five_calls_start_concurrently()
    {
        var tokenRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTokenResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/token")
            {
                tokenRequestStarted.TrySetResult();
                await releaseTokenResponse.Task.WaitAsync(cancellationToken);
                return TokenResponse("access-token", "20271206130000");
            }

            return PortfolioResponse();
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var calls = Enumerable.Range(0, 5)
            .Select(_ => client.GetPortfolioAsync("1234567890"))
            .ToArray();

        await tokenRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseTokenResponse.SetResult();
        await Task.WhenAll(calls);

        Assert.Equal(1, handler.Requests.Count(request => request.Path == "/oauth2/token"));
        Assert.Equal(5, handler.Requests.Count(request => request.Path == "/api/dostk/acnt"));
        Assert.Equal(5, handler.Requests.Count(request => request.ApiId == "kt00018"));
    }

    [Fact]
    public async Task GetPortfolioAsync_refreshes_the_token_and_retries_once_after_unauthorized()
    {
        var tokenRequestCount = 0;
        var accountEvaluationRequestCount = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/token")
            {
                tokenRequestCount++;
                return Task.FromResult(TokenResponse($"token-{tokenRequestCount}", "20271206130000"));
            }

            accountEvaluationRequestCount++;
            return Task.FromResult(accountEvaluationRequestCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : PortfolioResponse());
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.GetPortfolioAsync("1234567890");

        Assert.Equal(2, tokenRequestCount);
        Assert.Equal(2, accountEvaluationRequestCount);
        Assert.Equal(
            ["Bearer token-1", "Bearer token-2"],
            handler.Requests
                .Where(request => request.ApiId == "kt00018")
                .Select(request => request.Authorization ?? string.Empty)
                .ToArray());
    }

    private static KiwoomClient CreateClient(HttpClient httpClient, TimeProvider? timeProvider = null) =>
        new(
            httpClient,
            new FixedKiwoomAccountStore("1234567890", "app-key", "secret-key"),
            timeProvider ?? TimeProvider.System);

    private static HttpResponseMessage TokenResponse(string token, string expiresAt) =>
        JsonResponse($$"""{"token":"{{token}}","expires_dt":"{{expiresAt}}","return_code":0}""");

    private static HttpResponseMessage PortfolioResponse(
        string holdings = "[]",
        string totalPurchaseAmount = "0",
        string totalEvaluationAmount = "0",
        string totalProfitLoss = "0",
        string totalProfitRate = "0") =>
        JsonResponse($$"""{"tot_pur_amt":"{{totalPurchaseAmount}}","tot_evlt_amt":"{{totalEvaluationAmount}}","tot_evlt_pl":"{{totalProfitLoss}}","tot_prft_rt":"{{totalProfitRate}}","acnt_evlt_remn_indv_tot":{{holdings}}}""");

    private static HttpResponseMessage CashBalanceResponse(string depositAmount = "0") =>
        JsonResponse($$"""{"entr":"{{depositAmount}}"}""");

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class FixedKiwoomAccountStore(string expectedAccountNumber, string appKey, string secretKey) : IKiwoomAccountStore
    {
        public Task<IReadOnlyList<KiwoomAccountProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KiwoomAccountProfile>>([]);

        public Task<KiwoomAccountCredentials> GetAsync(string accountNumber, CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedAccountNumber, accountNumber);
            return Task.FromResult(new KiwoomAccountCredentials(
                new KiwoomAccountProfile(accountNumber, KiwoomEnvironment.Mock),
                appKey,
                secretKey));
        }

        public Task SaveAsync(KiwoomAccountCredentials account, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string accountNumber, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow += amount;
    }

    private sealed record RecordedRequest(string Path, string? Authorization, string? ApiId, string? Body);

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = [];

        public static StubHttpMessageHandler FromResponses(params HttpResponseMessage[] responses)
        {
            var responseQueue = new Queue<HttpResponseMessage>(responses);
            return new StubHttpMessageHandler((_, _) => Task.FromResult(responseQueue.Dequeue()));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new RecordedRequest(
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("api-id", out var apiIds) ? apiIds.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await responder(request, cancellationToken);
        }
    }
}
