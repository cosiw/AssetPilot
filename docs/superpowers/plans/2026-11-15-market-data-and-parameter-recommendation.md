# 시장 데이터 수집과 월간 파라미터 추천 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 키움 REST로 KODEX 200의 장 마감 일봉·5분봉을 수집해 공통 CSV에 안전하게 저장하고, 매월 승인 대기 상태의 워크포워드 파라미터 추천안을 만든다.

**Architecture:** 새 `AssetPilot.MarketDataWorker`는 Windows 작업 스케줄러가 호출하는 단발 실행 프로그램이다. `IMarketDataProvider`를 통해 키움 REST에서 받은 원본을 보관하고, `CsvMarketDataStore`가 검증·병합한 표준 CSV만 백테스트와 향후 실전 신호가 읽는다. 월말에는 기존 `BacktestRunner`로 후보를 평가하여 추천 이력만 기록하며, 별도의 승인 명령만 활성 설정을 바꾼다.

**Tech Stack:** .NET 10, C#, Microsoft.Extensions.Hosting, Serilog, xUnit, 키움 REST, UTF-8 CSV, Windows Task Scheduler.

---

## 파일 구성

| 파일 | 책임 |
| --- | --- |
| `src/AssetPilot.Backtest/MarketData/DailyMarketBar.cs` | 거래량을 포함한 표준 일봉 모델 |
| `src/AssetPilot.Backtest/MarketData/IntradayMarketBar.cs` | KST 시각·거래량을 포함한 표준 5분봉 모델 |
| `src/AssetPilot.Backtest/MarketData/IMarketDataProvider.cs` | 공급자 교체 포트 |
| `src/AssetPilot.Backtest/MarketData/CsvMarketDataStore.cs` | 검증, 중복 제거, 연도별 CSV 병합, 원자적 교체 |
| `src/AssetPilot.Backtest/MarketData/CsvMarketDataLoader.cs` | 표준 헤더와 레거시 샘플 형식을 모두 읽는 호환 로더 |
| `src/AssetPilot.Infrastructure/Kiwoom/KiwoomMarketDataProvider.cs` | 키움 일봉·5분봉 REST 어댑터 |
| `src/AssetPilot.Backtest/Strategy/WalkForwardParameterSelector.cs` | 후보 평가와 순위 결정 |
| `src/AssetPilot.Backtest/Strategy/ParameterDecisionStore.cs` | 추천/승인 이력과 활성 설정 저장 |
| `src/AssetPilot.MarketDataWorker/*` | 단발 수집·월말 추천 호스트 |
| `tests/AssetPilot.Backtest.Tests/*` | 데이터·저장소·추천 단위 및 통합 테스트 |
| `tests/AssetPilot.Core.Tests/KiwoomMarketDataProviderTests.cs` | 키움 HTTP 계약 테스트 |
| `docs/windows-task-scheduler.md` | 시장 데이터 Worker 배포·스케줄 절차 |

## Task 1: 표준 시장 데이터 계약과 표준 CSV 읽기

**Files:**

- Create: `src/AssetPilot.Backtest/MarketData/DailyMarketBar.cs`
- Create: `src/AssetPilot.Backtest/MarketData/IntradayMarketBar.cs`
- Create: `src/AssetPilot.Backtest/MarketData/IMarketDataProvider.cs`
- Modify: `src/AssetPilot.Backtest/MarketData/CsvMarketDataLoader.cs:6-96`
- Modify: `tests/AssetPilot.Backtest.Tests/CsvMarketDataLoaderTests.cs:1-55`

- [ ] **Step 1: 표준 헤더를 읽는 실패 테스트를 작성한다.**

```csharp
[Fact]
public void LoadDaily_reads_canonical_header_format()
{
    var path = WriteFile("daily.csv", "date,open,high,low,close,volume,source,updated_at\n2026-01-02,101,115,99,110,10,kiwoom,2026-01-02T16:05:00+09:00");

    var bars = new CsvMarketDataLoader().LoadDaily(path);

    Assert.Equal(new DailyBar(new DateOnly(2026, 1, 2), 101m, 115m, 99m, 110m), Assert.Single(bars));
}
```

- [ ] **Step 2: 테스트가 실패함을 확인한다.**

Run: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~CsvMarketDataLoaderTests.LoadDaily_reads_canonical_header_format" --no-restore`

Expected: FAIL. 현재 로더는 8열 레거시 순서를 일봉으로 해석한다.

- [ ] **Step 3: 표준 모델과 공급자 포트를 추가한다.**

```csharp
public sealed record DailyMarketBar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);
public sealed record IntradayMarketBar(DateTime Timestamp, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public interface IMarketDataProvider
{
    string Source { get; }
    Task<IReadOnlyList<DailyMarketBar>> GetDailyAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IntradayMarketBar>> GetFiveMinuteAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
```

`CsvMarketDataLoader`는 첫 줄을 `StringSplitOptions.TrimEntries`로 분해해 첫 다섯 열이 정확히 `date,open,high,low,close`이면 표준 형식으로 판정한다. 표준 일봉은 열 0~4, 표준 5분봉은 `date,time,open,high,low,close`의 열 0~5를 읽는다. 그 외 형식은 기존 `ParseDaily`와 `ParseFiveMinute` 로직을 그대로 사용한다.

- [ ] **Step 4: 새 테스트와 기존 로더 테스트를 실행한다.**

Run: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~CsvMarketDataLoaderTests" --no-restore`

Expected: PASS.

- [ ] **Step 5: 커밋한다.**

```text
git add src/AssetPilot.Backtest/MarketData tests/AssetPilot.Backtest.Tests/CsvMarketDataLoaderTests.cs
git commit -m "feat: define canonical market data contracts"
```

## Task 2: 검증·병합·원자적 CSV 저장소

**Files:**

- Create: `src/AssetPilot.Backtest/MarketData/CsvMarketDataStore.cs`
- Create: `tests/AssetPilot.Backtest.Tests/CsvMarketDataStoreTests.cs`

- [ ] **Step 1: 중복 대체와 무효 OHLC 거부 테스트를 작성한다.**

```csharp
[Fact]
public async Task MergeFiveMinuteAsync_replaces_the_same_timestamp_without_duplicate()
{
    var store = new CsvMarketDataStore(_root);
    var timestamp = new DateTime(2026, 1, 2, 9, 0, 0);
    await store.MergeFiveMinuteAsync("069500", [new(timestamp, 100m, 110m, 99m, 105m, 1)]);
    await store.MergeFiveMinuteAsync("069500", [new(timestamp, 101m, 111m, 100m, 106m, 2)]);

    var bars = new CsvMarketDataLoader().LoadFiveMinute(Path.Combine(_root, "market-data", "069500", "5m", "2026.csv"));

    Assert.Equal(new FiveMinuteBar(timestamp, 101m, 111m, 100m, 106m), Assert.Single(bars));
}

[Fact]
public async Task MergeDailyAsync_rejects_invalid_ohlc_without_changing_existing_file()
{
    var store = new CsvMarketDataStore(_root);
    await store.MergeDailyAsync("069500", [new(new DateOnly(2026, 1, 2), 100m, 110m, 90m, 105m, 1)]);

    await Assert.ThrowsAsync<ArgumentException>(() => store.MergeDailyAsync("069500", [new(new DateOnly(2026, 1, 3), 100m, 99m, 90m, 105m, 1)]));

    Assert.Single(new CsvMarketDataLoader().LoadDaily(Path.Combine(_root, "market-data", "069500", "daily.csv")));
}
```

- [ ] **Step 2: 실패를 확인한다.**

Run: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~CsvMarketDataStoreTests" --no-restore`

Expected: FAIL. `CsvMarketDataStore`가 아직 없다.

- [ ] **Step 3: 최소 저장소를 구현한다.**

`CsvMarketDataStore` 생성자는 데이터 루트를 받고 다음 공개 메서드를 제공한다.

```csharp
public Task MergeDailyAsync(string symbol, IEnumerable<DailyMarketBar> bars, CancellationToken ct = default);
public Task MergeFiveMinuteAsync(string symbol, IEnumerable<IntradayMarketBar> bars, CancellationToken ct = default);
```

각 메서드는 다음 순서를 지킨다.

1. `symbol`은 숫자 여섯 자리만 허용한다.
2. 가격은 양수, 거래량은 0 이상, `low <= min(open, close)` 및 `high >= max(open, close)`를 확인한다.
3. 5분봉은 초가 0이고 분이 5의 배수인지 확인한다.
4. 기존 표준 파일을 읽어 키별 dictionary로 만든 뒤, 새 값을 키 기준으로 대체한다.
5. 일봉은 `market-data/{symbol}/daily.csv`, 5분봉은 `market-data/{symbol}/5m/{year}.csv`로 나눈다.
6. UTF-8 헤더와 가격·거래량·`source=kiwoom`·`updated_at`을 기록한 `{path}.tmp`를 같은 폴더에 쓴다.
7. `File.Move(tmp, path, overwrite: true)`로 교체한다. 예외가 나면 tmp만 삭제하고 기존 파일은 건드리지 않는다.

- [ ] **Step 4: 병합 테스트를 실행한다.**

Run: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~CsvMarketDataStoreTests" --no-restore`

Expected: PASS.

- [ ] **Step 5: 커밋한다.**

```text
git add src/AssetPilot.Backtest/MarketData/CsvMarketDataStore.cs tests/AssetPilot.Backtest.Tests/CsvMarketDataStoreTests.cs
git commit -m "feat: store normalized market data as csv"
```

## Task 3: 키움 REST 시장 데이터 어댑터

**Files:**

- Create: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomMarketDataProvider.cs`
- Create: `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomDailyChartRequest.cs`
- Create: `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomMinuteChartRequest.cs`
- Create: `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomChartResponse.cs`
- Create: `tests/AssetPilot.Core.Tests/KiwoomMarketDataProviderTests.cs`

- [ ] **Step 1: 5분봉 요청과 연속 조회 테스트를 작성한다.**

```csharp
[Fact]
public async Task GetFiveMinuteAsync_requests_five_minute_scope_and_merges_continuation_pages()
{
    var handler = new RecordingHandler(
        TokenResponse(),
        ChartResponse("20261113", "090000", "51200", "51300", "51150", "51250", "10", continuation: "Y"),
        ChartResponse("20261113", "090500", "51250", "51400", "51200", "51350", "11", continuation: "N"));
    var provider = CreateProvider(handler);

    var bars = await provider.GetFiveMinuteAsync("069500", new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 13));

    Assert.Equal(2, bars.Count);
    Assert.Contains(handler.Requests, request => request.ApiId == "ka11080" && request.Body.Contains("\"tic_scope\":\"5\""));
}
```

- [ ] **Step 2: 실패를 확인한다.**

Run: `rtk dotnet test .\tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter "FullyQualifiedName~KiwoomMarketDataProviderTests" --no-restore`

Expected: FAIL. 시장 데이터 공급자가 아직 없다.

- [ ] **Step 3: 키움 어댑터를 구현한다.**

`KiwoomMarketDataProvider`는 기존 `KiwoomClient`와 같은 `HttpClient`, `IKiwoomAccountStore`, `TimeProvider` 의존성을 받아 해당 저장소의 첫 Real 계정 앱 키·시크릿으로 토큰을 얻는다. `Source`는 항상 `"kiwoom"`이다.

- 일봉은 키움 `ka10081` 일봉 조회를 호출한다.
- 5분봉은 키움 `ka11080`에 `tic_scope = "5"`로 호출한다.
- 응답이 연속 조회를 요구하면 같은 범위의 다음 키를 붙여 빈 페이지 또는 종료 표식까지 반복한다.
- 숫자는 키움의 부호 포함 문자열을 절댓값 decimal/long으로 파싱한다.
- HTTP 429와 5xx는 기존 클라이언트와 같은 횟수·지수 지연으로 재시도하고, 그 외 4xx와 잘못된 payload는 즉시 예외를 낸다.

토큰 발급·HTTP 공통 코드를 복제하지 말고 `KiwoomClient`에서 재사용 가능한 internal `PostAsync` 및 토큰 발급 구성 요소로 추출한다. 기존 계좌 API 테스트가 계속 통과해야 한다.

- [ ] **Step 4: 키움과 기존 클라이언트 테스트를 실행한다.**

Run: `rtk dotnet test .\tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter "FullyQualifiedName~KiwoomClientTests|FullyQualifiedName~KiwoomMarketDataProviderTests" --no-restore`

Expected: PASS.

- [ ] **Step 5: 커밋한다.**

```text
git add src/AssetPilot.Infrastructure/Kiwoom tests/AssetPilot.Core.Tests/KiwoomMarketDataProviderTests.cs
git commit -m "feat: fetch daily and five-minute bars from kiwoom"
```

## Task 4: 단발 MarketDataWorker와 원본 보관

**Files:**

- Create: `src/AssetPilot.MarketDataWorker/AssetPilot.MarketDataWorker.csproj`
- Create: `src/AssetPilot.MarketDataWorker/Program.cs`
- Create: `src/AssetPilot.MarketDataWorker/MarketDataRunner.cs`
- Create: `src/AssetPilot.MarketDataWorker/MarketDataSchedule.cs`
- Create: `tests/AssetPilot.MarketDataWorker.Tests/AssetPilot.MarketDataWorker.Tests.csproj`
- Create: `tests/AssetPilot.MarketDataWorker.Tests/MarketDataScheduleTests.cs`
- Modify: `AssetPilot.slnx`
- Modify: `docs/windows-task-scheduler.md`
- Modify: `.gitignore`

- [ ] **Step 1: 실행 가능 시간 테스트를 작성한다.**

```csharp
[Theory]
[InlineData("2026-11-13T16:04:59+09:00", false)]
[InlineData("2026-11-13T16:05:00+09:00", true)]
[InlineData("2026-11-14T16:05:00+09:00", false)]
public void IsEligible_uses_kst_weekday_and_market_close(string value, bool expected)
{
    Assert.Equal(expected, MarketDataSchedule.IsEligible(DateTimeOffset.Parse(value)));
}
```

- [ ] **Step 2: 실패를 확인한다.**

Run: `rtk dotnet test .\tests\AssetPilot.MarketDataWorker.Tests\AssetPilot.MarketDataWorker.Tests.csproj --filter "FullyQualifiedName~MarketDataScheduleTests" --no-restore`

Expected: FAIL. Worker 프로젝트와 `MarketDataSchedule`이 아직 없다.

- [ ] **Step 3: Worker를 구현한다.**

`MarketDataSchedule.IsEligible`는 `DateTimeOffset`을 KST로 변환한 뒤 월~금, `16:05 <= time < 24:00`만 true를 반환한다. `MarketDataRunner`는 false일 때 `IMarketDataProvider`를 호출하지 않고 성공 결과를 반환한다.

실행 가능하면 `today`와 직전 9거래일을 계산해 `069500`의 일봉·5분봉을 가져온다. HTTP 원문은 API 종류별 `data/raw/kiwoom/069500/{daily|5m}/{yyyy-MM-dd}.json`에 UTF-8로 먼저 저장하고, 그 뒤 `CsvMarketDataStore`를 호출한다. 새 Worker는 `%LOCALAPPDATA%\\AssetPilot\\data`를 기본 루트로 사용하되 `AssetPilot__MarketDataRoot` 환경 변수로 재정의할 수 있게 한다.

`Program.cs`는 기존 `SnapshotWorker`의 Serilog와 DI 패턴을 따라 `KiwoomMarketDataProvider`, `CsvMarketDataStore`, `MarketDataRunner`를 등록한다. 수집 실패는 종료 코드 1, 정상 수집과 시간 외 건너뛰기는 0이다.

`.gitignore`에는 `/data/`를 추가한다. `docs/windows-task-scheduler.md`에는 새 작업의 이름, 월~금 16:05 트리거, 실행 중 중복 금지, 실패 시 10분 간격 3회 재시도, Credential Manager 소유 사용자 실행을 추가한다.

- [ ] **Step 4: Worker 테스트와 빌드를 실행한다.**

Run: `rtk dotnet test .\tests\AssetPilot.MarketDataWorker.Tests\AssetPilot.MarketDataWorker.Tests.csproj --no-restore`

Expected: PASS.

Run: `rtk dotnet build .\src\AssetPilot.MarketDataWorker\AssetPilot.MarketDataWorker.csproj --no-restore`

Expected: `Build succeeded.`

- [ ] **Step 5: 커밋한다.**

```text
git add AssetPilot.slnx .gitignore src/AssetPilot.MarketDataWorker tests/AssetPilot.MarketDataWorker.Tests docs/windows-task-scheduler.md
git commit -m "feat: add scheduled market data worker"
```

## Task 5: 워크포워드 추천과 승인 상태 저장

**Files:**

- Create: `src/AssetPilot.Backtest/Strategy/ParameterRecommendation.cs`
- Create: `src/AssetPilot.Backtest/Strategy/WalkForwardParameterSelector.cs`
- Create: `src/AssetPilot.Backtest/Strategy/ParameterDecisionStore.cs`
- Create: `tests/AssetPilot.Backtest.Tests/WalkForwardParameterSelectorTests.cs`
- Create: `tests/AssetPilot.Backtest.Tests/ParameterDecisionStoreTests.cs`
- Modify: `src/AssetPilot.MarketDataWorker/MarketDataRunner.cs`

- [ ] **Step 1: 이력 부족과 미승인 추천 테스트를 작성한다.**

```csharp
[Fact]
public void Select_returns_insufficient_history_when_three_year_train_window_is_missing()
{
    var result = new WalkForwardParameterSelector().Select(CreateDailyBars(days: 200), CreateFiveMinuteBars(days: 200), new DateOnly(2026, 11, 30));

    Assert.Equal(ParameterDecisionStatus.InsufficientHistory, result.Status);
    Assert.Null(result.Options);
}

[Fact]
public async Task SaveRecommendation_does_not_change_active_parameters_until_approved()
{
    var store = new ParameterDecisionStore(_root);
    await store.SaveRecommendationAsync(PendingRecommendation());

    Assert.False(File.Exists(Path.Combine(_root, "strategy-state", "active-parameters.json")));
}
```

- [ ] **Step 2: 실패를 확인한다.**

Run: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~WalkForwardParameterSelectorTests|FullyQualifiedName~ParameterDecisionStoreTests" --no-restore`

Expected: FAIL. 추천기와 결정 저장소가 아직 없다.

- [ ] **Step 3: 추천 모델·선정기·저장소를 구현한다.**

`ParameterDecisionStatus`는 `InsufficientHistory`, `PendingApproval`, `Approved`, `Rejected` 네 값만 가진다. 추천기는 기준일 직전 3년을 학습, 그 뒤 6개월을 검증에 사용하고 완료 거래가 20건 미만인 후보를 제외한다. 후보 그리드는 명세에 고정한 SMA·기울기·이격·단기 SMA·ATR·밴드·목표·손절·보유일 값을 사용한다.

각 후보는 `new BacktestRunner().Run(...)`과 `new SwingAtrStrategy(options)`로 검증 구간을 평가한다. 후보는 검증 수익률 내림차순, Profit Factor 내림차순, 최대 낙폭 오름차순으로 정렬한다. 최고 후보는 `PendingApproval` 추천으로, 없으면 `InsufficientHistory`로 반환한다.

`ParameterDecisionStore`는 `strategy-state/parameter-decisions.csv`에 추천을 append한다. 열은 `decision_id,decision_date,effective_from,symbol,status,options_json,train_from,train_to,validation_from,validation_to,completed_trades,validation_return_pct,profit_factor,max_drawdown_pct,reason,approved_at,approved_by`다.

`ApproveAsync(decisionId, approvedBy)`는 해당 행이 `PendingApproval`일 때만 `Approved`로 바꾸고, `active-parameters.json`을 임시 파일 후 `File.Move(..., overwrite: true)`로 저장한다. `RejectAsync`는 `Rejected`로만 바꾸고 활성 파일을 건드리지 않는다.

- [ ] **Step 4: 월말 연결 테스트를 추가하고 실행한다.**

`MarketDataRunner`가 당월 마지막 거래일이며 CSV 병합이 성공했을 때만 추천기를 호출하는 테스트를 추가한다. 이력이 부족한 경우에도 Worker는 종료 코드 0으로 끝나고 `InsufficientHistory` 행을 하나 남겨야 한다.

Run: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~WalkForwardParameterSelectorTests|FullyQualifiedName~ParameterDecisionStoreTests" --no-restore`

Expected: PASS.

- [ ] **Step 5: 커밋한다.**

```text
git add src/AssetPilot.Backtest/Strategy src/AssetPilot.MarketDataWorker/MarketDataRunner.cs tests/AssetPilot.Backtest.Tests
git commit -m "feat: recommend monthly atr strategy parameters"
```

## Task 6: 전체 회귀 검증과 운영 문서 마무리

**Files:**

- Modify: `docs/backtest-trend-filters.md`
- Modify: `docs/windows-task-scheduler.md`
- Modify: `README.md`

- [ ] **Step 1: 비용 미반영과 승인 경계를 문서화한다.**

`docs/backtest-trend-filters.md`에는 추천이 비용·세금·슬리피지를 반영하지 않으며 결과가 자동 적용되지 않는다는 문장을 추가한다. `README.md`에는 `data/`가 운영 데이터이며 Git에 올리지 않는다는 설명, 표준 CSV 헤더, `active-parameters.json`은 승인된 값만 포함한다는 설명을 추가한다.

- [ ] **Step 2: 전체 테스트를 실행한다.**

Run: `rtk dotnet test .\AssetPilot.slnx --no-restore`

Expected: 모든 테스트 PASS.

- [ ] **Step 3: 공백과 추적 상태를 확인한다.**

Run: `rtk git diff --check`

Expected: 출력 없음, 종료 코드 0.

Run: `rtk git status --short`

Expected: 이 계획에 속한 파일만 표시된다. 기존 사용자의 변경은 삭제하거나 되돌리지 않는다.

- [ ] **Step 4: 최종 커밋한다.**

```text
git add README.md docs/backtest-trend-filters.md docs/windows-task-scheduler.md
git commit -m "docs: document market data operations"
```

## 계획 자체 검토

- 명세의 원본·정규화 분리, 10거래일 재동기화, 키 기반 중복 제거, 원자적 저장은 Task 2와 4가 구현한다.
- 공급자 교체 포트와 키움 첫 구현은 Task 1과 3이 구현한다.
- 단발 Worker, KST 장마감 조건, 스케줄러 운용은 Task 4가 구현한다.
- 3년 학습·6개월 검증·최소 20건·사람 승인 경계는 Task 5가 구현한다.
- 주문·WebSocket·AI 자동 적용은 어떤 작업에도 추가하지 않는다.
- 이 문서에는 구현자가 결정해야 할 미정 항목을 남기지 않았다. 타입명과 파일 경로는 모든 작업에서 동일하게 사용한다.
