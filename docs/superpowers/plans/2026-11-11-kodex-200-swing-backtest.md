# KODEX 200 전략 백테스트 구현 계획

> **에이전트 작업자용:** 작업은 작은 TDD 단위로 실행한다. 각 기능은 실패 테스트를 먼저 확인하고 최소 구현으로 통과시킨다. 사용자가 요청하지 않았으므로 커밋하지 않는다.

**목표:** KODEX 200 일봉·5분봉 CSV와 선택한 전략으로 백테스트를 실행하고, 체결 원장·핵심 지표·저장된 실행 기록을 WPF에서 확인·비교한다.

**구조:** `AssetPilot.Backtest`는 전략, 시세 로딩, 시간순 체결, 결과 계산을 담당한다. `AssetPilot.Infrastructure`는 SQLite의 실행·체결·진단 저장을 담당한다. `AssetPilot.App`은 실행 설정, 결과 상세, 기록 목록을 제공한다. 한 실행에는 전략 하나만 사용하며, 전략별 숫자 설정은 실행 결과에 JSON으로 고정 저장한다.

**기술:** .NET 10, C#, WPF, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit.

---

### 작업 1: 전략과 백테스트 도메인 모델

**파일:**

- 생성: `src/AssetPilot.Backtest/MarketData/DailyBar.cs`
- 생성: `src/AssetPilot.Backtest/MarketData/FiveMinuteBar.cs`
- 생성: `src/AssetPilot.Backtest/Strategy/ITradingStrategy.cs`
- 생성: `src/AssetPilot.Backtest/Strategy/StrategyOptions.cs`
- 생성: `src/AssetPilot.Backtest/Strategy/EntryPlan.cs`
- 생성: `src/AssetPilot.Backtest/Strategy/ExitPlan.cs`
- 생성: `src/AssetPilot.Backtest/Strategy/SwingAtrStrategy.cs`
- 테스트: `tests/AssetPilot.Backtest.Tests/SwingAtrStrategyTests.cs`

- [ ] **1단계: ATR 단일 진입의 신호·매수 구간·청산 가격 실패 테스트를 작성한다.**

```csharp
[Fact]
public void EvaluateAtClose_returns_a_buy_band_when_close_is_above_120_day_sma()
{
    var strategy = new SwingAtrStrategy(new StrategyOptions());
    var plan = strategy.EvaluateAtClose(TestBars.Daily(120, close: 10_000m, high: 10_200m, low: 9_800m));

    Assert.Equal(9_800m, plan!.BuyLowerPrice);
    Assert.Equal(10_200m, plan.BuyUpperPrice);
    Assert.Equal(400m, plan.Atr);
}

[Fact]
public void CreateExitPlan_uses_the_actual_entry_price()
{
    var exit = new SwingAtrStrategy(new StrategyOptions()).CreateExitPlan(9_900m, 400m);
    Assert.Equal(10_500m, exit.TargetPrice);
    Assert.Equal(9_500m, exit.StopPrice);
}
```

- [ ] **2단계: 전략 타입 부재로 실패하는지 확인한다.**

실행: `rtk dotnet test tests\AssetPilot.Backtest.Tests --filter FullyQualifiedName~SwingAtrStrategyTests`

기대: `SwingAtrStrategy`와 계약 타입 부재로 컴파일 실패.

- [ ] **3단계: 최소 계약과 `SwingAtrStrategy`를 구현한다.**

`ITradingStrategy`는 `Key`, `DisplayName`, `EvaluateAtClose`, `CreateExitPlan`을 제공한다. 기본 옵션은 SMA 120, ATR 14, 매수 구간 반폭 0.5 ATR, 익절 1.5 ATR, 손절 1.0 ATR이다. 지표 미준비 또는 종가가 SMA 이하이면 진입 계획을 만들지 않는다.

- [ ] **4단계: 전략 테스트 전체를 통과시킨다.**

실행: `rtk dotnet test tests\AssetPilot.Backtest.Tests --filter FullyQualifiedName~SwingAtrStrategyTests`

기대: PASS.

### 작업 2: CSV 로더와 시간순 체결 엔진

**파일:**

- 생성: `src/AssetPilot.Backtest/MarketData/CsvMarketDataLoader.cs`
- 생성: `src/AssetPilot.Backtest/Execution/BacktestRunner.cs`
- 생성: `src/AssetPilot.Backtest/Execution/BacktestRequest.cs`
- 생성: `src/AssetPilot.Backtest/Execution/BacktestResult.cs`
- 생성: `src/AssetPilot.Backtest/Execution/BacktestExecution.cs`
- 생성: `src/AssetPilot.Backtest/Execution/BacktestDiagnostic.cs`
- 테스트: `tests/AssetPilot.Backtest.Tests/CsvMarketDataLoaderTests.cs`
- 테스트: `tests/AssetPilot.Backtest.Tests/BacktestRunnerTests.cs`

- [ ] **1단계: 제공한 CSV 열 순서와 오름차순 정렬의 실패 테스트를 작성한다.**

일봉은 0번 일자, 1번 종가, 5번(F·시가), 6번(G·고가), 7번(H·저가)를 사용한다. 5분봉은 0~5번의 날짜·시간·시가·고가·저가·종가를 사용한다. 최신순 입력을 오름차순 결과로 바꾼다는 테스트를 작성한다.

- [ ] **2단계: 로더 부재로 실패하는지 확인한다.**

실행: `rtk dotnet test tests\AssetPilot.Backtest.Tests --filter FullyQualifiedName~CsvMarketDataLoaderTests`

기대: `CsvMarketDataLoader` 부재로 컴파일 실패.

- [ ] **3단계: 외부 패키지 없는 로더를 구현한다.**

따옴표와 천 단위 구분기호가 포함된 숫자를 파싱하고, `yyyy/MM/dd` 날짜를 처리한다. 필수 값 오류는 파일명·줄 번호를 가진 `FormatException`으로 알린다.

- [ ] **4단계: 다음 네 가지 실패 테스트를 작성한다.**

```csharp
[Fact] public void Run_records_600_profit_when_target_is_hit() { /* 10000 매수, 10600 익절 */ }
[Fact] public void Run_exits_at_open_when_stop_is_gapped_through() { /* 9600 손절, 9300 시가 */ }
[Fact] public void Run_records_only_stop_when_both_prices_are_touched_in_one_bar() { }
[Fact] public void Run_skips_same_day_reentry_after_an_intraday_exit() { }
```

- [ ] **5단계: `BacktestRunner`를 구현한다.**

시작 자금과 매수 비율로 `floor(사용 가능 현금 × 매수 비율 ÷ 체결가)` 수량을 계산한다. 매수 계획은 다음 거래일 모든 5분봉에서 검사한다. 시가가 구간 안이면 시가, 시가가 하한 아래에서 고가가 하한 도달이면 하한, 시가가 상한 위에서 저가가 상한 도달이면 상한에 체결한다. 시가 체결은 같은 봉부터, 봉 내부 경계 체결은 다음 봉부터 청산을 검사한다. 청산 우선순위는 시가 손절, 시가 익절, 저가 손절, 고가 익절이다. 같은 봉 양쪽 도달은 손절 우선이다.

`BacktestExecution`은 매수·매도를 각각 한 행으로 반환한다. 매수 뒤와 매도 뒤의 보유 수량·평균 매수가·현금을 기록하며, 매도에서만 실현 손익을 기록한다. 포지션은 메모리에서 체결 원장을 재생해 관리한다.

- [ ] **6단계: 데이터 누락 및 전체 엔진 테스트를 통과시킨다.**

예약된 거래일에 5분봉이 없으면 다른 날로 넘기지 않고 `MissingIntradayData` 진단을 만든다.

실행: `rtk dotnet test tests\AssetPilot.Backtest.Tests`

기대: PASS.

### 작업 3: 실행 기록 SQLite 저장소

**파일:**

- 생성: `src/AssetPilot.Backtest/Storage/IBacktestRunStore.cs`
- 생성: `src/AssetPilot.Backtest/Storage/BacktestRunSummary.cs`
- 생성: `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs`
- 수정: `src/AssetPilot.Infrastructure/AssetPilot.Infrastructure.csproj`
- 수정: `tests/AssetPilot.Backtest.Tests/AssetPilot.Backtest.Tests.csproj`
- 테스트: `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs`

- [ ] **1단계: 실행·체결·진단을 저장하고 다시 읽는 실패 테스트를 작성한다.**

```csharp
[Fact]
public async Task SaveAsync_round_trips_run_summary_executions_and_diagnostics()
{
    await store.SaveAsync(runWithTwoExecutionsAndOneDiagnostic);
    var detail = await store.GetDetailAsync(runWithTwoExecutionsAndOneDiagnostic.Id);
    Assert.Equal(2, detail!.Executions.Count);
    Assert.Single(detail.Diagnostics);
}
```

- [ ] **2단계: 저장소 타입 부재로 실패하는지 확인한다.**

실행: `rtk dotnet test tests\AssetPilot.Backtest.Tests --filter FullyQualifiedName~SqliteBacktestRunStoreTests`

기대: 컴파일 실패.

- [ ] **3단계: 세 테이블과 저장소를 구현한다.**

`AssetPilot.Infrastructure`와 백테스트 테스트 프로젝트에 `AssetPilot.Backtest` 및 필요한 Infrastructure 프로젝트 참조를 추가한다. `backtest_runs`에는 전략 키·버전·설정 JSON·종목·시작/종료 자금·입력 파일 메타데이터·기간·상태·요약 지표를 저장한다. `backtest_executions`에는 실행 ID와 내부 거래 사이클 ID, 체결 원장 값을 저장한다. `backtest_diagnostics`에는 경고·오류를 저장한다. 금액·수량은 decimal 정밀도 보존을 위해 TEXT, 시각은 ISO 8601 TEXT로 저장한다. 외래 키와 `run_id`, `executed_at` 인덱스를 만든다.

- [ ] **4단계: 목록 조회와 비교용 요약 조회 실패 테스트를 추가한다.**

`GetSummariesAsync`는 최신 실행부터 목록을 반환하고, `GetSummariesByIdsAsync`는 선택한 최대 세 실행을 반환한다.

- [ ] **5단계: 저장소 테스트 전체를 통과시킨다.**

실행: `rtk dotnet test tests\AssetPilot.Backtest.Tests --filter FullyQualifiedName~SqliteBacktestRunStoreTests`

기대: PASS.

### 작업 4: WPF 실행·상세·기록 ViewModel

**파일:**

- 생성: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`
- 생성: `src/AssetPilot.App/ViewModels/BacktestHistoryViewModel.cs`
- 생성: `src/AssetPilot.App/ViewModels/BacktestExecutionRowViewModel.cs`
- 수정: `src/AssetPilot.App/App.xaml.cs`
- 수정: `src/AssetPilot.App/AssetPilot.App.csproj`
- 테스트: `tests/AssetPilot.App.Tests/BacktestRunViewModelTests.cs`
- 테스트: `tests/AssetPilot.App.Tests/BacktestHistoryViewModelTests.cs`

- [ ] **1단계: 실행 ViewModel의 설정 검증 실패 테스트를 작성한다.**

시작 자금이 0 이하이거나 자금 투입 비율이 0 초과 100 이하가 아니면 실행하지 않고 오류 메시지를 표시한다. 유효한 경로·설정에서는 로더·러너·저장소를 호출하고 요약과 체결 행을 채운다는 테스트를 작성한다.

- [ ] **2단계: ViewModel 타입 부재로 실패하는지 확인한다.**

실행: `rtk dotnet test tests\AssetPilot.App.Tests --filter FullyQualifiedName~BacktestRunViewModelTests`

기대: 컴파일 실패.

- [ ] **3단계: 실행 ViewModel을 구현한다.**

전략 선택 목록에는 첫 버전의 `ATR 단일 진입`만 표시한다. 설정값, 시작 자금, 매수 비율, 일봉·5분봉 경로를 노출한다. 실행 뒤 누적 손익/수익률, 완료 거래, 승률, Profit Factor, MDD, 최대 연속 손절, 평균 수익·손실, 데이터 상태와 체결 원장을 채운다. 모든 결과에 `비용 미반영`을 표시한다.

- [ ] **4단계: 기록 목록·상세 이동·비교 선택 실패 테스트를 작성한다.**

저장소 목록을 최신순으로 표시하고, 실행 하나를 선택하면 상세를 로드하며, 비교 선택은 세 건을 초과하지 않는다는 테스트를 작성한다.

- [ ] **5단계: 기록 ViewModel을 구현하고 테스트를 통과시킨다.**

실행: `rtk dotnet test tests\AssetPilot.App.Tests --filter FullyQualifiedName~Backtest`

기대: PASS.

### 작업 5: WPF 화면과 전체 검증

**파일:**

- 생성: `src/AssetPilot.App/BacktestWindow.xaml`
- 생성: `src/AssetPilot.App/BacktestWindow.xaml.cs`
- 수정: `src/AssetPilot.App/MainWindow.xaml`
- 수정: `src/AssetPilot.App/MainWindow.xaml.cs`
- 수정: `src/AssetPilot.App/App.xaml.cs`

- [ ] **1단계: 기존 테마를 사용해 백테스트 창을 구현한다.**

실행 화면에는 파일 경로, 전략 선택, 시작 자금, 전략별 설정값, 실행 버튼을 둔다. 결과에는 8개 요약 지표와 `비용 미반영` 레이블, 매수/매도 한 줄씩의 체결 DataGrid, 진단·미청산 상태를 표시한다. 기록 화면에는 실행 목록, 정렬, 최대 세 개 비교 선택, 행 클릭 상세 이동을 둔다.

- [ ] **2단계: DI와 탐색을 연결한다.**

`App.xaml.cs`에서 전략, CSV 로더, 러너, SQLite 저장소, 두 ViewModel, 창을 등록한다. `MainWindow`에서 백테스트 창을 여는 명시적 버튼을 추가한다.

- [ ] **3단계: 전체 테스트와 빌드를 실행한다.**

실행: `rtk dotnet test`

실행: `rtk dotnet build`

기대: PASS.

- [ ] **4단계: 제공 CSV로 수동 실행을 확인한다.**

`samples/market-data/KODEX 200.csv`, `samples/market-data/5분봉.csv`, 시작 자금 10,000,000원, 투입 비율 20%, ATR 단일 진입으로 실행한다. 체결 원장, 요약, 데이터 누락 진단, 저장된 기록이 화면에 표시되는지 확인한다.
