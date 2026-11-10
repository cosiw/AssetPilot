# 일별 계좌 스냅샷 구현 계획

> **에이전트 작업자용:** 반드시 `superpowers:subagent-driven-development`(권장) 또는 `superpowers:executing-plans` 하위 스킬을 사용해 작업별로 실행한다. 진행 단계는 체크박스로 관리한다.

**목표:** Windows 작업 스케줄러가 실행하는 Worker로 모든 키움 계좌의 일별 자산 스냅샷과 중복 없는 거래 원장을 로컬 SQLite에 저장한다.

**아키텍처:** 짧게 실행되는 `AssetPilot.SnapshotWorker`가 Core 포트와 Infrastructure 어댑터를 조합한다. Core는 KST 시간 조건과 저장 흐름을, Infrastructure는 Credential Manager 마이그레이션·키움 REST·SQLite를 담당한다. WPF 앱은 작업 스케줄러로 실행하지 않는다.

**기술:** .NET 10, C#, Microsoft.Extensions.Hosting, Microsoft.Data.Sqlite, Windows Credential Manager, xUnit, 키움 REST `kt00018`·`kt00015`.

---

## 파일 구성

- 수정: `AssetPilot.slnx` — `AssetPilot.SnapshotWorker` 프로젝트 추가.
- 수정: `src/AssetPilot.Core/Accounts/KiwoomAccountProfile.cs` — 불투명한 `Guid AccountId` 추가.
- 수정: `src/AssetPilot.Core/Portfolio/Portfolio.cs` — `CashBalance`, `TotalAssetAmount` 추가.
- 생성: `src/AssetPilot.Core/Transactions/AccountTransaction.cs`, `TransactionType.cs`, `IKiwoomTransactionClient.cs` — 정규화된 거래 원장 계약.
- 생성: `src/AssetPilot.Core/Snapshots/AccountSnapshot.cs`, `PositionSnapshot.cs`, `IAccountSnapshotStore.cs`, `SnapshotSchedule.cs`, `AccountSnapshotRunner.cs` — 저장소 비종속 워크플로우.
- 수정: `src/AssetPilot.Infrastructure/Accounts/KiwoomCredentialPayload.cs`, `WindowsKiwoomAccountStore.cs` — AccountId 저장과 기존 Credential 마이그레이션.
- 수정: `src/AssetPilot.Infrastructure/Kiwoom/KiwoomClient.cs`, `Contracts/KiwoomAccountEvaluationResponse.cs` — 확장 `kt00018`, `kt00015` 클라이언트.
- 생성: `src/AssetPilot.Infrastructure/Kiwoom/Contracts/KiwoomConsignedTransactionRequest.cs`, `KiwoomConsignedTransactionResponse.cs` — `kt00015` 통신 DTO.
- 생성: `src/AssetPilot.Infrastructure/Snapshots/SqliteAccountSnapshotStore.cs` — 스키마와 트랜잭션 저장.
- 생성: `src/AssetPilot.SnapshotWorker/AssetPilot.SnapshotWorker.csproj`, `Program.cs` — 단발 실행 호스트.
- 수정/생성 테스트: `WindowsCredentialFormatTests.cs`, `KiwoomClientTests.cs`, `AccountSnapshotRunnerTests.cs`, `SnapshotScheduleTests.cs`, `SqliteAccountSnapshotStoreTests.cs`.
- 생성: `docs/windows-task-scheduler.md` — 작업 스케줄러 설정 문서.

### 작업 1: 모든 Credential에 안정적인 AccountId 저장

**대상:** Core 계좌 프로필, Credential payload/store, `WindowsCredentialFormatTests.cs`.

- [ ] **1단계: 기존 Credential 마이그레이션 실패 테스트를 작성한다.**

```csharp
[Fact]
public async Task GetAllAsync_backfills_a_stable_account_id_for_a_legacy_credential()
{
    var store = CreateStoreWithLegacyCredential();
    var first = Assert.Single(await store.GetAllAsync());
    var second = Assert.Single(await store.GetAllAsync());
    Assert.NotEqual(Guid.Empty, first.AccountId);
    Assert.Equal(first.AccountId, second.AccountId);
}
```

- [ ] **2단계: RED 확인.** `rtk dotnet test tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter FullyQualifiedName~WindowsCredentialFormatTests`를 실행한다. `AccountId` 누락 컴파일 실패가 예상된다.

- [ ] **3단계: 최소 구현.** 프로필을 `KiwoomAccountProfile(Guid AccountId, string AccountNumber, KiwoomEnvironment Environment)`로 변경한다. JSON blob에 nullable `Guid? AccountId`를 추가한다. `SaveAsync`는 새 GUID를 쓰고, `GetAsync`/`GetAllAsync`는 기존 blob에 GUID가 없으면 App Key·Secret Key·환경을 보존한 채 한 번만 다시 쓴다.

- [ ] **4단계: GREEN 확인.** 대상 테스트를 재실행한다. 손상 blob은 계속 건너뛰고 예외에 Secret Key가 노출되지 않는지도 검증한다.

- [ ] **5단계: 커밋.** `git add src/AssetPilot.Core/Accounts src/AssetPilot.Infrastructure/Accounts tests/AssetPilot.Core.Tests/WindowsCredentialFormatTests.cs && git commit -m "feat: add stable account IDs to credentials"`

### 작업 2: Core 스냅샷·거래·시간 조건 계약 정의

**대상:** `Portfolio.cs`, 신규 `Core/Transactions/*`, `Core/Snapshots/*`, Runner/Schedule 테스트.

- [ ] **1단계: 부호 있는 현금과 KST 시간 조건 실패 테스트를 작성한다.**

```csharp
[Theory]
[InlineData("2026-11-09T06:59:00+00:00", false)]
[InlineData("2026-11-09T07:00:00+00:00", true)]
[InlineData("2026-11-14T08:00:00+00:00", false)]
public void IsEligible_returns_expected_value(string utc, bool expected)
    => Assert.Equal(expected, SnapshotSchedule.IsEligible(DateTimeOffset.Parse(utc)));
```

- [ ] **2단계: RED 확인.** `rtk dotnet test tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter "FullyQualifiedName~SnapshotScheduleTests|FullyQualifiedName~AccountSnapshotRunnerTests"`를 실행한다.

- [ ] **3단계: 계약 구현.** `TransactionType`은 `Deposit`, `Withdrawal`, `Buy`, `Sell`, `Dividend`, `Other`를 가진다. `AccountTransaction`은 거래일·선택 UTC 시각·증권사 원본 유형·선택 종목 정보·선택 수량/단가·부호 있는 `long CashAmount`·음수가 아닌 수수료/세금·`SourceTransactionKey`를 가진다. `Portfolio`에는 `long CashBalance`, `long TotalAssetAmount`를 추가한다. 금액은 원 단위 `long`, 수익률은 퍼센트 소수 둘째 자리 정수 `int`를 사용한다.

- [ ] **4단계: 시간 조건 구현.** UTC를 `+09:00`으로 변환하고 월~금 및 `16:00 <= 시간 < 24:00`일 때만 `SnapshotSchedule.IsEligible`이 true를 반환하게 한다.

- [ ] **5단계: GREEN 확인 및 커밋.** 대상 테스트 통과 후 `feat: define snapshot contracts`로 커밋한다.

### 작업 3: 어댑터보다 먼저 Runner 동작 구현

**대상:** `AccountSnapshotRunner.cs`, `AccountSnapshotRunnerTests.cs`.

- [ ] **1단계: 기존 스냅샷은 건너뛰되 거래는 동기화하는 실패 테스트를 작성한다.**

```csharp
[Fact]
public async Task RunAsync_skips_existing_snapshot_but_still_upserts_transactions()
{
    var store = new FakeSnapshotStore { SnapshotExists = true };
    var runner = CreateRunner(store, transactions: [Dividend("key-1")]);
    var result = await runner.RunAsync();
    Assert.True(result.Succeeded);
    Assert.Equal(0, runner.PortfolioCalls);
    Assert.Single(store.UpsertedTransactions);
}
```

- [ ] **2단계: RED 확인.** 대상 Runner 테스트를 실행한다.

- [ ] **3단계: 흐름 구현.** 허용 시간이 아니면 API 호출 없이 성공 종료한다. 허용 시간에는 모든 계좌를 열거하고, 오늘 KST 스냅샷이 없을 때만 `kt00018`을 조회해 요약과 종목을 저장한다. 모든 계좌에서 `kt00015`를 조회한다. 거래 원장이 비어 있으면 오늘만, 있으면 마지막 저장 거래일부터 오늘까지 포함해 조회한다. 계좌 하나가 실패해도 다른 계좌는 계속 처리하고, 하나라도 실패하면 전체 결과는 실패다.

- [ ] **4단계: GREEN 확인.** 빈 포트폴리오, 계좌 하나 실패/다른 계좌 성공, 다음 실행에서 거래 재시도, 과거 스냅샷 미생성을 테스트한다.

- [ ] **5단계: 커밋.** `feat: run daily account snapshot workflow` 메시지로 커밋한다.

### 작업 4: 공식 JSON 계약으로 키움 어댑터 확장

**대상:** 키움 DTO/client, `KiwoomClientTests.cs`.

- [ ] **1단계: 키움 공식 `kiwoom_api_spec.json`에서 `kt00015` 정확한 필드를 확인한다.** 요청 기간 필드, 연속조회 헤더/표시, 거래 식별자, 날짜/시간, 유형, 종목, 수량, 단가, 현금, 수수료, 세금 필드를 DTO에 복사한다. 한글 라벨을 추측하지 않는다. 기준 자료는 [키움 공식 API 저장소](https://github.com/Kiwoom-Securities/Kiwoom-REST-API)다.

- [ ] **2단계: 확장 `kt00018` 현금/총자산과 2페이지 `kt00015`에 대한 HTTP 실패 테스트를 작성한다.**

```csharp
[Fact]
public async Task GetTransactionsAsync_reads_all_pages_and_creates_stable_keys()
{
    using var client = CreateHttpClient(TokenResponse(), TransactionPageOne(), TransactionPageTwo());
    var items = await CreateClient(client).GetTransactionsAsync("1234567890", new(2026, 11, 1), new(2026, 11, 9));
    Assert.Equal(2, items.Count);
    Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.SourceTransactionKey)));
}
```

- [ ] **3단계: RED 확인.** `rtk dotnet test tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter FullyQualifiedName~KiwoomClientTests`를 실행한다.

- [ ] **4단계: 최소 구현.** 기존 인증 `PostAsync`를 재사용하고 `KiwoomClient`가 두 포트를 모두 구현하게 한다. 공식 연속조회 규칙을 그대로 적용한다. 모르는 원본 거래 유형은 원본 값을 보존하며 `Other`로 매핑한다. 증권사 거래 ID가 있으면 `SourceTransactionKey`로, 없으면 정규화한 불변 원본 필드의 SHA-256을 사용한다.

- [ ] **5단계: GREEN 확인 및 커밋.** 모든 키움 테스트를 통과시키고 `feat: read Kiwoom account transactions`로 커밋한다.

### 작업 5: SQLite에 스냅샷과 거래를 원자적으로 저장

**대상:** Infrastructure csproj, `SqliteAccountSnapshotStore.cs`, 저장소 테스트.

- [ ] **1단계: 멱등성 실패 테스트를 작성한다.**

```csharp
[Fact]
public async Task SaveSnapshot_and_upsert_transactions_are_idempotent()
{
    await using var db = await TestDatabase.CreateAsync();
    var store = new SqliteAccountSnapshotStore(db.ConnectionString);
    await store.InitializeAsync();
    await store.SaveSnapshotAsync(CreateSnapshot());
    await store.SaveSnapshotAsync(CreateSnapshot());
    await store.UpsertTransactionsAsync([Dividend("same-key")]);
    await store.UpsertTransactionsAsync([Dividend("same-key")]);
    Assert.Equal(1, await db.CountAsync("account_snapshots"));
    Assert.Equal(1, await db.CountAsync("account_transactions"));
}
```

- [ ] **2단계: RED 확인.** `rtk dotnet test tests\AssetPilot.Core.Tests\AssetPilot.Core.Tests.csproj --filter FullyQualifiedName~SqliteAccountSnapshotStoreTests`를 실행한다.

- [ ] **3단계: `Microsoft.Data.Sqlite` 10.0.0과 스키마를 추가한다.** 승인된 설계의 `account_snapshots`, `position_snapshots`, `account_transactions`를 `CREATE TABLE IF NOT EXISTS`로 만든다. 매 연결에서 외래키를 켜고 `UNIQUE(account_id, snapshot_date)`, `UNIQUE(account_id, source_transaction_key)`를 적용한다.

- [ ] **4단계: 저장 구현.** 헤더와 종목 행은 하나의 트랜잭션으로 저장하고, 날짜 중복 시 변경 없이 종료한다. 거래는 `ON CONFLICT(account_id, source_transaction_key) DO NOTHING`을 쓴다. KST 날짜는 `yyyy-MM-dd`, UTC 시각은 왕복 가능한 ISO-8601 문자열로 저장한다.

- [ ] **5단계: GREEN 확인 및 커밋.** 빈 포트폴리오, 중복 종목 롤백, 중복 거래, 최신 거래일 테스트를 추가해 통과시키고 `feat: persist account history in sqlite`로 커밋한다.

### 작업 6: 작업 스케줄러용 Worker와 운영 문서 추가

**대상:** 신규 Worker 프로젝트, `docs/windows-task-scheduler.md`.

- [ ] **1단계: `net10.0-windows` 콘솔 프로젝트를 생성한다.** Core/Infrastructure와 `Microsoft.Extensions.Hosting` 10.0.14를 참조한다. Credential Manager, 계좌 Store, HttpClient, 두 포트의 KiwoomClient, `%LOCALAPPDATA%\AssetPilot\assetpilot.db`의 SQLite Store, Runner를 등록한다.

- [ ] **2단계: `Program.cs`를 구현한다.** SQLite 초기화 후 한 번 실행하고, 비밀 없는 건수 요약만 출력한다. 성공/의도적 건너뛰기는 `0`, 계좌 하나라도 실패하면 `1`을 반환한다.

- [ ] **3단계: 빌드와 건너뛰기 경로를 검증한다.** `rtk dotnet build src\AssetPilot.SnapshotWorker\AssetPilot.SnapshotWorker.csproj --no-restore` 후 허용 시간 밖에 Worker를 실행한다. 키움 호출 없이 종료 코드 `0`이어야 한다.

- [ ] **4단계: 작업 스케줄러 문서를 작성한다.** 월~금, 16:00 시작, 1시간 반복, 8시간 유지, Credential 소유 Windows 사용자, 놓친 작업 즉시 실행, 실패 시 10분 간격 3회 재시도를 명시한다. 인수에는 계좌 정보나 비밀값을 넣지 않는다.

- [ ] **5단계: 전체 검증 및 커밋.** `rtk dotnet test AssetPilot.slnx --no-restore`, `git diff --check` 실행 후 Worker/문서를 `feat: add scheduled account snapshot worker`로 커밋한다.

## 계획 자체 점검

- AccountId 마이그레이션, 전체 계좌 처리, 주말/시간 조건, 과거 스냅샷 미생성, `kt00018` 예수금/총자산, `kt00015` 거래 원장, 중복 방지, SQLite 원자성, 오류 격리, Worker, 작업 스케줄러를 모두 포함한다.
- 휴장일 판정, WPF 이력 화면, 장중 다중 스냅샷은 범위 밖으로 유지한다.
- `kt00015` DTO는 구현 직전에 키움 공식 JSON 스펙에서 정확한 와이어 필드를 가져와 작성하므로 추측 필드를 코드에 넣지 않는다.
