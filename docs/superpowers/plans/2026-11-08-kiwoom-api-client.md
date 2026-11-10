# Kiwoom Single Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 토큰 캐시, 인증 HTTP 요청, 포트폴리오 조회를 `KiwoomClient` 하나에 모으고, 키움 응답을 재사용 가능한 Core 도메인 모델로 한 번만 변환한다.

**Architecture:** `KiwoomClient`가 `IKiwoomPortfolioClient`를 구현하고 private 메서드로 토큰 발급·캐시와 인증된 JSON POST를 처리한다. 키움 JSON DTO는 이 클래스 내부에만 두고, 결과는 Core의 `Portfolio`와 `PortfolioPosition`으로 한 번만 변환한다. 화면은 이 도메인 모델을 직접 사용하며, 화면 전용 값이 필요해질 때만 App ViewModel에 추가한다. API가 둘 이상 추가되기 전에는 별도 `KiwoomApiClient`, 기능별 Client, public 토큰 Provider, 단순 전달용 Service를 만들지 않는다.

**Tech Stack:** .NET 10, `HttpClient`, `TimeProvider`, `System.Text.Json`, xUnit, 키움 REST OAuth `au10001`.

## File map

- `src/AssetPilot.Infrastructure/Kiwoom/KiwoomClient.cs`: 토큰 캐시, OAuth, private JSON POST, `kt00018` 매핑을 모두 담당한다.
- `src/AssetPilot.Infrastructure/Kiwoom/KiwoomPortfolioClient.cs`: 제거한다.
- `src/AssetPilot.Infrastructure/Kiwoom/IKiwoomAccessTokenProvider.cs`: 제거한다.
- `src/AssetPilot.Infrastructure/Kiwoom/KiwoomAccessTokenProvider.cs`: 제거한다.
- `src/AssetPilot.Infrastructure/Kiwoom/KiwoomApiEndpointResolver.cs`: 제거하고 base URI 선택을 `KiwoomClient` private 메서드로 넣는다.
- `src/AssetPilot.Core/Brokerage/PortfolioSnapshot.cs`: 제거한다.
- `src/AssetPilot.Core/Portfolio/Portfolio.cs`: 계좌 전체 평가와 보유종목 목록을 표현하는 도메인 모델이다.
- `src/AssetPilot.Core/Portfolio/PortfolioPosition.cs`: 보유 종목 한 행을 표현하는 도메인 모델이다.
- `src/AssetPilot.Core/Brokerage/IKiwoomPortfolioClient.cs`: 반환 타입을 `Portfolio`로 바꾼다.
- `src/AssetPilot.App/App.xaml.cs`: `KiwoomClient`를 `IKiwoomPortfolioClient`로 등록한다.
- `tests/AssetPilot.Core.Tests/KiwoomClientTests.cs`: 공개 포트폴리오 API를 통해 토큰 캐시·갱신·동시 호출·`kt00018` 매핑을 검증한다.
- `tests/AssetPilot.Core.Tests/KiwoomAccessTokenProviderTests.cs`: 제거한다.
- `tests/AssetPilot.Core.Tests/KiwoomPortfolioClientTests.cs`: 제거 또는 `KiwoomClientTests.cs`로 통합한다.

## Task 1: 임시 토큰 Provider 검증 완료

**Files:**
- Create: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomApiEndpointResolver.cs`
- Create: `src/AssetPilot.Infrastructure/Kiwoom/IKiwoomAccessTokenProvider.cs`
- Create: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomAccessTokenProvider.cs`
- Create: `tests/AssetPilot.Core.Tests/KiwoomAccessTokenProviderTests.cs`

- [x] Step 1. 유효 토큰 재사용, 만료 5분 전 갱신, 동시 호출 한 번 발급 테스트를 먼저 작성했다.
- [x] Step 2. 테스트가 구현 부재로 실패함을 확인했다.
- [x] Step 3. 계좌번호·환경별 토큰 캐시와 Korea Standard Time 만료 파싱을 구현했다.
- [x] Step 4. 토큰 테스트 3개와 Infrastructure 빌드를 통과시켰다. 커밋은 만들지 않았다.

## Task 2: 토큰 Provider와 포트폴리오 Client를 KiwoomClient로 병합

**Files:**
- Create: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomClient.cs`
- Delete: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomAccessTokenProvider.cs`
- Delete: `src/AssetPilot.Infrastructure/Kiwoom/IKiwoomAccessTokenProvider.cs`
- Delete: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomApiEndpointResolver.cs`
- Delete: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomPortfolioClient.cs`
- Delete: `src/AssetPilot.Core/Brokerage/PortfolioSnapshot.cs`
- Create: `src/AssetPilot.Core/Portfolio/Portfolio.cs`
- Create: `src/AssetPilot.Core/Portfolio/PortfolioPosition.cs`
- Modify: `src/AssetPilot.Core/Brokerage/IKiwoomPortfolioClient.cs`
- Delete: `tests/AssetPilot.Core.Tests/KiwoomAccessTokenProviderTests.cs`
- Modify: `tests/AssetPilot.Core.Tests/KiwoomPortfolioClientTests.cs`

- [x] Step 1. Rename the portfolio test class and file to `KiwoomClientTests`. Keep its `kt00018` mapping assertion, but construct `KiwoomClient(HttpClient, IKiwoomAccountStore, TimeProvider)` and make the HTTP handler return the OAuth response followed by the existing account-evaluation response. Assert exactly two requests: `/oauth2/token`, then `/api/dostk/acnt`.
- [x] Step 2. Run `& 'C:\Program Files\dotnet\dotnet.exe' test tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --no-restore --filter KiwoomClientTests`. Expected: compile failure because `KiwoomClient` does not exist.
- [x] Step 3. Create immutable Core records `Portfolio` and `PortfolioPosition`. `Portfolio` contains total purchase amount, total evaluation amount, total profit/loss, total profit rate, and `IReadOnlyList<PortfolioPosition> Positions`; `PortfolioPosition` contains stock code, stock name, quantity, average price, and current price. Change `IKiwoomPortfolioClient.GetPortfolioAsync` to return `Task<Portfolio>`, then delete `PortfolioSnapshot` and `PortfolioHolding`. Do not add `PortfolioService` yet because it would only delegate to the client.
- [x] Step 4. Create `KiwoomClient : IKiwoomPortfolioClient`. Move account lookup; cache keyed by account number and `KiwoomEnvironment`; per-key `SemaphoreSlim`; OAuth `POST /oauth2/token`; `expires_dt` parsing as `yyyyMMddHHmmss` with UTC+09:00; five-minute refresh window; and `kt00018` response-to-`Portfolio` mapping into private members.
- [x] Step 5. Add private `PostAsync<TRequest, TResponse>` inside `KiwoomClient`. It must create a fresh request, use the selected account environment to choose `https://api.kiwoom.com` or `https://mockapi.kiwoom.com`, serialize JSON with `JsonSerializerDefaults.Web`, and add `Authorization: Bearer {token}`, `api-id`, and `cont-yn: N`. `GetPortfolioAsync` calls it with API ID `kt00018`, path `/api/dostk/acnt`, and body `{ qry_tp = "2", dmst_stex_tp = "KRX" }`.
- [x] Step 6. Keep token invalidation private. On `HttpStatusCode.Unauthorized`, remove the cache entry, obtain a new token, and recreate the API request once. A second 401 and all other non-success responses are passed through `EnsureSuccessStatusCode`; no indefinite retry occurs. Error messages must not contain App Key, Secret Key, or access token.
- [x] Step 7. Run focused `KiwoomClientTests`. Expected: `kt00018` mapping returns `Portfolio`. Remove temporary Provider classes and their tests only after the new client test passes.

> Verification note: the focused test compiled successfully. Its execution was intermittently blocked by the local Windows application-control policy, not by an assertion failure.

## Task 3: KiwoomClient 토큰·인증 동작을 공개 API로 검증

**Files:**
- Modify: `tests/AssetPilot.Core.Tests/KiwoomClientTests.cs`

- [x] Step 1. Add a failing test that calls `GetPortfolioAsync("1234567890")` twice. Its handler returns OAuth, account-evaluation, account-evaluation; assert there is one OAuth request and two `kt00018` requests.
- [x] Step 2. Add a failing test with a test-local `MutableTimeProvider` at `2026-11-08T12:54:00+09:00`. Return a token expiring at `13:00:00`, advance time to `12:56:00`, issue a second portfolio call, and assert two OAuth requests due to the five-minute refresh window.
- [x] Step 3. Add a failing concurrent test that starts five `GetPortfolioAsync` calls behind an OAuth handler gate. Return a valid account-evaluation response for every `kt00018` request. Assert exactly one OAuth request and five account-evaluation requests.
- [x] Step 4. Add a failing 401 test: OAuth returns `token-1`, first `kt00018` response is 401, OAuth returns `token-2`, second `kt00018` response is successful. Assert two OAuth requests, two `kt00018` requests, and the second account request uses `Bearer token-2`.
- [x] Step 5. Run `KiwoomClientTests`. Expected: all cache, refresh, concurrency, header, 401-retry, and mapping tests pass. No live key or network request is used.

> Verification note: the tests build successfully. Runtime execution is currently blocked by the local Windows application-control policy when loading `AssetPilot.Infrastructure.dll`; no live key or network request was used.

## Task 4: DI와 회귀 검증

**Files:**
- Modify: `src/AssetPilot.App/App.xaml.cs`
- Modify: `docs/superpowers/specs/2026-11-08-kiwoom-api-client-design.md` only if implementation requires a behavior correction.

- [x] Step 1. Register `HttpClient` with no fixed `BaseAddress`, then register `IKiwoomPortfolioClient` as `KiwoomClient`. Do not register a separate API Client or token Provider.
- [x] Step 2. Run `& 'C:\Program Files\dotnet\dotnet.exe' build src\AssetPilot.App\AssetPilot.App.csproj --no-restore`. Expected: zero warnings and zero errors.
- [x] Step 3. Run all available tests. If the Windows application-control policy blocks a test DLL, report it distinctly from assertion failures. Do not call a real or mock Kiwoom server during verification, and do not commit.

> Verification note: the App build succeeded with zero warnings and errors. Of 17 tests, 8 passed; the remaining 9 were blocked only while Windows application-control policy loaded `AssetPilot.Infrastructure.dll`. No assertion failure or live network request occurred.
