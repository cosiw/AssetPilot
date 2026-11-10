# 메인 화면 자산 기록 및 예수금 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 키움 예수금과 로컬 스냅샷 기록을 기존 WPF 메인 창에 표시한다.

**Architecture:** `KiwoomClient`의 각 메서드는 Kiwoom API 하나만 호출한다. `GetPortfolioAsync`는 `kt00018`, `GetCashBalanceAsync`는 `kt00001`을 호출하고, `AccountSnapshotRunner`가 두 결과를 조합해 예수금 수집 여부를 포함한 스냅샷을 저장한다. 앱의 메인 화면은 Kiwoom API를 호출하지 않으며, 별도 읽기 인터페이스를 통해 SQLite만 읽어 상단 요약·보유 종목·자산 기록 차트·구성을 같은 스냅샷 기준으로 만든다.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, LiveChartsCore.SkiaSharpView.WPF 2.0.5, xUnit.

**User direction:** 이 작업은 테스트 주도 방식으로 진행하지 않는다. 각 작업을 구현한 뒤 관련 단위 테스트와 빌드로 검증하며, 사용자의 요청에 따라 커밋하지 않는다.

---

## 파일 구조

| 파일 | 책임 |
|---|---|
| `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomCashBalanceResponse.cs` | `kt00001` 예수금 JSON 응답 매핑 |
| `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomCashBalanceRequest.cs` | `kt00001` 조회 구분 요청 매핑 |
| `src/AssetPilot.Infrastructure/Kiwoom/KiwoomClient.cs` | `kt00018` 포트폴리오와 `kt00001` 예수금을 각각 별도 메서드로 조회 |
| `src/AssetPilot.Core/Brokerage/IKiwoomCashBalanceClient.cs` | `kt00001` 예수금 조회 계약 |
| `src/AssetPilot.Core/Snapshots/AccountSnapshotRunner.cs` | 포트폴리오와 예수금을 스냅샷으로 조합 |
| `src/AssetPilot.Core/Snapshots/AccountSnapshot.cs` | 예수금 수집 여부 보존 |
| `src/AssetPilot.Core/Snapshots/AccountSnapshotSummary.cs` | 화면용 스냅샷 요약 읽기 모델 |
| `src/AssetPilot.Core/Snapshots/AccountSnapshotDetail.cs` | 화면용 스냅샷 헤더·종목 상세 읽기 모델 |
| `src/AssetPilot.Core/Snapshots/IAccountSnapshotReader.cs` | 스냅샷 읽기 계약 |
| `src/AssetPilot.Infrastructure/Snapshots/SqliteAccountSnapshotStore.cs` | 스키마 마이그레이션과 날짜 범위 요약 조회 |
| `src/AssetPilot.App/ViewModels/AssetHistoryViewModel.cs` | 기간·기준일·차트·구성 상태 |
| `src/AssetPilot.App/ViewModels/MainViewModel.cs` | 최신 스냅샷 기반 요약·보유 종목과 자산 기록 로드 연결 |
| `src/AssetPilot.App/App.xaml.cs` | 앱 DB와 스냅샷 리더 DI 등록 |
| `src/AssetPilot.App/MainWindow.xaml` | 요약 여섯 칸 및 자산 기록 영역 |

### Task 1: 키움 예수금 조회를 스냅샷 수집 흐름에 분리

**Files:**
- Create: `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomCashBalanceResponse.cs`
- Create: `src/AssetPilot.Core/Brokerage/IKiwoomCashBalanceClient.cs`
- Modify: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomClient.cs`
- Modify: `src/AssetPilot.Core/Snapshots/AccountSnapshotRunner.cs`
- Modify: `src/AssetPilot.SnapshotWorker/Program.cs`
- Modify: `tests/AssetPilot.Core.Tests/KiwoomClientTests.cs`
- Modify: `tests/AssetPilot.Core.Tests/AccountSnapshotRunnerTests.cs`

- [x] `KiwoomCashBalanceResponse`에 `[JsonPropertyName("entr")] public string? DepositAmount { get; init; }`를 선언했다.
- [x] `IKiwoomCashBalanceClient.GetCashBalanceAsync` 계약을 선언했다.
- [x] `GetPortfolioAsync`는 `kt00018`만 `PostAsync` 호출하고, `GetCashBalanceAsync`는 `kt00001`만 `{"qry_tp":"3"}` 요청으로 `PostAsync` 호출하도록 분리했다.
- [x] `AccountSnapshotRunner`가 두 클라이언트를 호출하여 `AccountSnapshot.CashBalance`에 예수금을 저장하고, SnapshotWorker DI에 두 계약을 같은 `KiwoomClient` 인스턴스로 등록했다.
- [x] 포트폴리오와 예수금 메서드가 각각 하나의 `api-id`만 요청하는지, 토큰 재사용·갱신·재시도가 유지되는지, 스냅샷 러너가 예수금을 저장하는지를 검증했다.
- [x] `rtk dotnet test .\tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter "FullyQualifiedName~KiwoomClientTests|FullyQualifiedName~AccountSnapshotRunnerTests"`를 실행했다. 10개 테스트가 통과했다.
- [x] `rtk dotnet build .\src\AssetPilot.SnapshotWorker\AssetPilot.SnapshotWorker.csproj --no-restore`를 실행했다. 오류와 경고 없이 통과했다.

### Task 2: 예수금 수집 여부와 스냅샷 읽기 계약 추가

**Files:**
- Create: `src/AssetPilot.Core/Snapshots/AccountSnapshotSummary.cs`
- Create: `src/AssetPilot.Core/Snapshots/IAccountSnapshotReader.cs`
- Modify: `src/AssetPilot.Core/Snapshots/AccountSnapshot.cs`
- Modify: `src/AssetPilot.Core/Snapshots/AccountSnapshotRunner.cs`
- Modify: `tests/AssetPilot.Core.Tests/AccountSnapshotRunnerTests.cs`

- [x] `AccountSnapshot`의 `CashBalance` 다음에 `bool HasCashBalance`를 추가했다.
- [x] `AccountSnapshotSummary`를 설계 문서의 필드 순서 그대로 선언했다.
- [x] `IAccountSnapshotReader.GetSummariesAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken)`를 선언했다. `from`과 `to`는 각각 포함 범위이며 `null`은 제한 없음이다.
- [x] `AccountSnapshotRunner.CreateSnapshot`이 예수금 API 조회 성공 뒤 만들어진 모든 새 스냅샷에 `HasCashBalance: true`를 넣는다.
- [x] 러너 테스트의 예상 `AccountSnapshot`에 새 인수를 추가하고, 저장된 스냅샷의 `HasCashBalance`가 `true`인지 검증했다.
- [x] `rtk dotnet test .\tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter "FullyQualifiedName~KiwoomClientTests|FullyQualifiedName~AccountSnapshotRunnerTests|FullyQualifiedName~SqliteAccountSnapshotStoreTests"`를 실행했다. 13개 테스트가 통과했다.

### Task 3: SQLite 마이그레이션과 자산 기록 조회 구현

**Files:**
- Modify: `src/AssetPilot.Infrastructure/Snapshots/SqliteAccountSnapshotStore.cs`
- Modify: `tests/AssetPilot.Core.Tests/SqliteAccountSnapshotStoreTests.cs`

- [x] 새 DB의 `account_snapshots` DDL에 `cash_balance_captured INTEGER NOT NULL` 열을 `cash_balance` 다음에 추가했다.
- [x] 초기화 시 `PRAGMA table_info(account_snapshots)`로 열 존재 여부를 확인한다. 없으면 정확히 다음 SQL을 실행해 기존 DB를 보존한다.

```sql
ALTER TABLE account_snapshots
ADD COLUMN cash_balance_captured INTEGER NOT NULL DEFAULT 0;
```

- [x] 스냅샷 INSERT에 `$cashBalanceCaptured`를 추가하고, `HasCashBalance ? 1 : 0`을 저장한다.
- [x] `SqliteAccountSnapshotStore`가 `IAccountSnapshotReader`도 구현하게 하고, 날짜 오름차순 `AccountSnapshotSummary`를 반환한다. `from`/`to`가 null일 때는 해당 조건을 SQL에서 제외한다.

```sql
SELECT snapshot_date, captured_at_utc, cash_balance, cash_balance_captured,
       total_asset_amount, total_purchase_amount, total_evaluation_amount,
       total_profit_loss, total_profit_rate_hundredths
FROM account_snapshots
WHERE account_id = $accountId
  AND snapshot_date >= $from
  AND snapshot_date <= $to
ORDER BY snapshot_date;
```

- [x] 기존 스키마를 먼저 만들고 `InitializeAsync`를 다시 호출하는 테스트를 추가했다. 새 열의 기본값이 0이며 기존 행을 읽을 수 있다.
- [x] `HasCashBalance = true`와 false인 행을 저장·조회하는 테스트를 추가했다.
- [x] `rtk dotnet test .\tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter FullyQualifiedName~SqliteAccountSnapshotStoreTests`를 실행했다. 5개 테스트가 통과했다.

### Task 4: 앱에 DB 스냅샷 리더와 자산 기록 ViewModel 등록

**Files:**
- Create: `src/AssetPilot.App/ViewModels/AssetHistoryRange.cs`
- Create: `src/AssetPilot.App/ViewModels/AssetHistoryViewModel.cs`
- Create: `src/AssetPilot.Core/Snapshots/AccountSnapshotDetail.cs`
- Modify: `src/AssetPilot.Core/Snapshots/IAccountSnapshotReader.cs`
- Modify: `src/AssetPilot.Infrastructure/Snapshots/SqliteAccountSnapshotStore.cs`
- Modify: `src/AssetPilot.App/App.xaml.cs`
- Modify: `src/AssetPilot.App/ViewModels/MainViewModel.cs`
- Modify: `tests/AssetPilot.App.Tests/MainViewModelTests.cs`
- Create: `tests/AssetPilot.App.Tests/AssetHistoryViewModelTests.cs`

- [x] `AssetHistoryRange`에 `Week`, `Month`, `ThreeMonths`, `All`을 선언했다.
- [x] `AssetHistoryViewModel`은 `IAccountSnapshotReader`를 받고 다음 상태를 노출한다: `SelectedRange`, `AvailableSnapshotDates`, `SelectedSnapshotDate`, `Summaries`, `HasHistory`, `HasCashBalanceForSelectedDate`, `EmptyMessage`. 범위 기준일은 최신 저장 날짜이므로 `TimeProvider`는 필요하지 않아 의존성에서 제외했다.
- [x] 범위 시작일은 최신 스냅샷 날짜에서 각각 6일, 1개월 전, 3개월 전으로 계산한다. `All`은 제한을 두지 않는다. 기준일은 조회된 최신 날짜로 초기화하고, 사용자가 선택한 날짜가 범위를 벗어나면 최신 날짜로 되돌린다.
- [x] 종목 표도 DB 기준으로 만들기 위해 `AccountSnapshotDetail`과 `IAccountSnapshotReader.GetSnapshotAsync`를 추가하고 SQLite 구현에 종목 상세 조회를 추가했다.
- [x] `MainViewModel` 생성자에 `IAccountSnapshotReader`와 `AssetHistoryViewModel`을 추가하고 `AssetHistory` 속성으로 노출했다. 초기 계좌 선택 및 선택 계좌 변경 때 최신 스냅샷 및 그 종목 스냅샷을 읽어 기존 `Portfolio` 형식의 표시 모델을 채우고 `AssetHistory.LoadAsync(account.AccountId)`를 호출한다. 메인 화면의 `IKiwoomPortfolioClient` 의존성은 제거했고, 기존 새로고침 명령은 Kiwoom 대신 DB 스냅샷만 다시 읽는다.
- [x] `App.xaml.cs`에서 `%LOCALAPPDATA%\AssetPilot\assetpilot.db` 연결 문자열을 구성하고, 하나의 `SqliteAccountSnapshotStore` 인스턴스를 `IAccountSnapshotStore`와 `IAccountSnapshotReader` 양쪽에 등록했다. 화면 진입 시 스키마 초기화도 수행한다.
- [x] 이력 없는 계좌, 날짜 세 개인 계좌의 1주 범위, 최신 스냅샷의 `Portfolio` 매핑을 `FakeSnapshotReader`로 검증했다. 메인 ViewModel에는 키움 클라이언트 의존성이 없다.
- [x] `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj`를 실행했다. 13개 테스트가 통과했다.

### Task 5: 메인 창의 예수금·자산 기록 UI와 차트 추가

**Files:**
- Modify: `src/AssetPilot.App/AssetPilot.App.csproj`
- Modify: `src/AssetPilot.App/MainWindow.xaml`
- Modify: `src/AssetPilot.App/Themes/Components/Surfaces.xaml` (필요한 자산 기록 패널 스타일만)
- Modify: `tests/AssetPilot.App.Tests/PortfolioDataGridLayoutTests.cs`

- [x] 앱 프로젝트에 안정 패키지 `LiveChartsCore.SkiaSharpView.WPF` 버전 `2.0.5`를 추가한다.
- [x] 기존 본문을 세로 `ScrollViewer`로 감싸고, 기존 계좌 요약을 여섯 열로 바꾼다. 첫 열은 총자산 `Portfolio.TotalAssetAmount`, 세 번째 열은 예수금 `Portfolio.CashBalance`로 바인딩한다. 나머지 평가금액·매입금액·평가손익·수익률은 유지한다. 요약 카드 위에는 최신 스냅샷의 한국 시간 기준 시각을 표시하고, 실시간·현재가라는 문구는 사용하지 않는다.
- [x] 보유 종목 표 아래에 `자산 기록` 패널을 만든다. 패널 헤더 안에만 1주·1개월·3개월·전체 버튼과 기준일 ComboBox를 배치하고, 계좌 선택 줄에는 넣지 않는다.
- [x] `CartesianChart`에 총자산과 예수금 선 시리즈를 바인딩한다. 예수금 미수집 날짜는 nullable 값으로 만들어 선을 끊고, 범례에 `예수금 저장 전` 안내를 보인다.
- [x] 기준일 스냅샷의 평가금액과 예수금으로 두 조각 `PieChart`를 바인딩한다. 기준일에 `HasCashBalance`가 false이거나 두 금액 합계가 0이면 PieChart 대신 정확한 빈 상태 문구를 표시한다.
- [x] DataGrid 열 구성 검증을 총자산·예수금 요약 바인딩과 자산 기록 컨트롤의 존재까지 확장했다. XAML은 프로젝트 빌드로 검증했다.
- [x] `rtk dotnet build AssetPilot.slnx --no-restore`와 `rtk dotnet test AssetPilot.slnx --no-restore`를 실행했다.

## 구현 순서와 의존성

```text
Task 1 ─┬─> Task 2 ─> Task 3 ─> Task 4 ─> Task 5
        └───────────────────────────────────────┘
```

예수금의 진실한 값이 먼저 `Portfolio`에 들어와야 새 스냅샷을 올바르게 표시할 수 있다. 스냅샷 읽기 계약과 DB 마이그레이션이 완료되어야 앱 ViewModel과 UI가 안정된 데이터 모델을 사용할 수 있다.

## 최종 검증

- `kt00001`이 실패할 때 현재 보유 현황과 그 계좌의 예약 스냅샷이 성공으로 처리되지 않는지 확인한다.
- 신규 DB와 기존 `assetpilot.db`에서 모두 초기화가 성공하는지 확인한다.
- 예수금 미수집 과거 스냅샷에 0원이나 0%를 표시하지 않는지 확인한다.
- DB가 없는 새 PC·저장 이력이 없는 계좌·스냅샷이 한 개인 계좌에서 빈 상태 또는 단일 포인트 차트가 정상 표시되는지 확인한다.
