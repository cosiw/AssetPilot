# 백테스트 체결 원장 및 CSV 내보내기 구현 계획

> **에이전트 작업자용:** 이 계획은 `superpowers:executing-plans`를 사용해 작업별로 구현한다. 완료한 항목은 체크박스로 표시한다.

**목표:** 백테스트의 매수·매도를 각각 체결로 보존하고, 체결로부터 포지션 상태와 완료 보유 구간을 계산해 SQLite 및 CSV로 제공한다. 기준 전략의 진입 조건은 변경하지 않는다.

**구조:** `BacktestRunner`는 순서가 있는 모의 체결만 생성한다. `PositionLedger`는 체결 목록을 받아 가중평균 평단 기준의 포지션 스냅샷과 포지션 사이클을 계산한다. 결과는 `BacktestResult`와 `BacktestRun`에 담고, SQLite를 원본으로 저장한다. CSV는 저장된 백테스트 실행 하나에서 재생성한다.

**기술:** C# / .NET 10, xUnit, Microsoft.Data.Sqlite, WPF

---

## 범위와 고정 원칙

- 고정 진입값: 장기 SMA 120, 단기 SMA 5, ATR 14, SMA 기울기 5일, 최소 SMA 이격 1%, 매수 밴드 0.5 ATR, 기존 5분봉 재확인.
- 변경 가능한 값: 익절 ATR, 손절 ATR, 최대 보유일 세 개.
- 현재 러너의 단일 포지션·손절 우선 체결 규칙은 바꾸지 않는다.
- 체결이 원본이며, 스냅샷과 사이클은 체결의 파생 결과다. 매수·매도를 합쳐 하나의 거래로 저장하지 않는다.
- 평단은 가중평균 방식이다. 수수료·세금 필드는 지금 보관하되 러너는 별도 거래비용 기능 전까지 `0m`으로 기록한다.
- MFE/MAE, 시가평가 자산곡선, 성과지표, ATR 그리드, 실제 주문·체결, FIFO 로트, UI 표는 다음 개발 단위로 미룬다.

## 파일 책임

| 파일 | 책임 |
| --- | --- |
| `src/AssetPilot.Backtest/Execution/BacktestExecution.cs` | 원본 체결과 식별자·순번·금액 |
| `src/AssetPilot.Backtest/Execution/BacktestPositionSnapshot.cs` | 체결 직후 포지션 상태 |
| `src/AssetPilot.Backtest/Execution/BacktestPositionCycle.cs` | 수량 0→0의 완료 보유 구간 |
| `src/AssetPilot.Backtest/Execution/PositionLedger.cs` | 가중평균 회계·사이클 계산 |
| `src/AssetPilot.Backtest/Execution/BacktestResult.cs` | 체결과 파생 결과 |
| `src/AssetPilot.Backtest/Execution/BacktestRunner.cs` | 체결 순번 부여·원장 적용 |
| `src/AssetPilot.Backtest/Storage/BacktestRun.cs` | 저장용 실행 집계 |
| `src/AssetPilot.Backtest/Storage/IBacktestRunExporter.cs` | CSV 내보내기 계약 |
| `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs` | 스키마 확장·저장·조회 |
| `src/AssetPilot.Infrastructure/Backtests/BacktestRunCsvExporter.cs` | CSV 파일 생성 |
| `tests/AssetPilot.Backtest.Tests/PositionLedgerTests.cs` | 원장 계산 단위 테스트 |
| `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs` | SQLite 호환·저장 테스트 |
| `tests/AssetPilot.Backtest.Tests/BacktestRunCsvExporterTests.cs` | CSV 형식·순서·인코딩 테스트 |

## 확정 데이터 모델

```csharp
public sealed record BacktestExecution(
    Guid ExecutionId, int Sequence, DateTime ExecutedAt,
    ExecutionSide Side, decimal Price, decimal Quantity,
    decimal GrossAmount, decimal Fee, decimal Tax,
    decimal? RealizedProfit = null, ExitReason? Reason = null);

public sealed record BacktestPositionSnapshot(
    Guid ExecutionId, int ExecutionSequence, DateTime RecordedAt,
    decimal Cash, decimal Quantity, decimal AverageEntryPrice,
    decimal PositionCost, decimal RealizedProfitTotal);

public sealed record BacktestPositionCycle(
    Guid CycleId, Guid OpeningExecutionId, Guid ClosingExecutionId,
    DateTime OpenedAt, DateTime ClosedAt, decimal BoughtQuantity,
    decimal SoldQuantity, decimal AverageEntryPrice, decimal AverageExitPrice,
    decimal RealizedProfit, decimal ReturnRate, ExitReason? ClosingReason);
```

`Sequence`는 러너가 생성한 순서대로 1부터 부여한다. 같은 시각의 체결도 이 순번으로 정렬한다. `GrossAmount`는 `Price * Quantity`여야 하며, 가격·수량은 0보다 크고 수수료·세금은 0 이상이어야 한다.

### 작업 1: 순수 체결 원장 구현

**파일:**

- 생성: `tests/AssetPilot.Backtest.Tests/PositionLedgerTests.cs`
- 생성: `src/AssetPilot.Backtest/Execution/BacktestPositionSnapshot.cs`
- 생성: `src/AssetPilot.Backtest/Execution/BacktestPositionCycle.cs`
- 생성: `src/AssetPilot.Backtest/Execution/PositionLedger.cs`
- 수정: `src/AssetPilot.Backtest/Execution/BacktestExecution.cs`

- [ ] **실패 테스트를 작성한다.**

```csharp
[Fact]
public void Apply_records_weighted_average_cost_and_partial_sale_profit()
{
    var ledger = new PositionLedger(1_000_000m);
    ledger.Apply(Buy(1, "2026-01-02T09:00:00", 10_000m, 100m));
    ledger.Apply(Buy(2, "2026-01-02T10:00:00", 10_200m, 50m));
    var sale = ledger.Apply(Sell(3, "2026-01-02T11:00:00", 10_600m, 80m));

    Assert.Equal(150m, ledger.Snapshots[1].Quantity);
    Assert.Equal(10_066.666666666666666666666667m, ledger.Snapshots[1].AverageEntryPrice);
    Assert.Equal(70m, sale.Snapshot.Quantity);
    Assert.Equal(42_666.666666666666666666666640m, sale.Execution.RealizedProfit);
}

[Fact]
public void Apply_creates_a_cycle_only_when_quantity_returns_to_zero()
{
    var ledger = new PositionLedger(1_000_000m);
    ledger.Apply(Buy(1, "2026-01-02T09:00:00", 10_000m, 100m));
    ledger.Apply(Buy(2, "2026-01-02T10:00:00", 10_200m, 50m));
    ledger.Apply(Sell(3, "2026-01-02T11:00:00", 10_600m, 80m));
    ledger.Apply(Sell(4, "2026-01-02T12:00:00", 10_400m, 70m, ExitReason.Time));

    Assert.Single(ledger.CompletedCycles);
    Assert.Equal(0m, ledger.Snapshots[^1].Quantity);
    Assert.Equal(ExitReason.Time, ledger.CompletedCycles[0].ClosingReason);
}

[Fact]
public void Apply_rejects_an_oversell_without_changing_state()
{
    var ledger = new PositionLedger(1_000_000m);
    ledger.Apply(Buy(1, "2026-01-02T09:00:00", 10_000m, 100m));
    Assert.Throws<InvalidOperationException>(() => ledger.Apply(Sell(2, "2026-01-02T10:00:00", 10_000m, 101m)));
    Assert.Single(ledger.Snapshots);
}
```

`Buy`·`Sell` 도우미는 `GrossAmount = price * quantity`, 수수료·세금 0, 결정적인 GUID와 입력 순번을 가진 체결을 만든다.

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~PositionLedgerTests --no-restore`

예상: 새 모델과 `PositionLedger`가 없어 컴파일 실패.

- [ ] **최소 구현을 작성한다.**

```csharp
public sealed class PositionLedger
{
    public PositionLedger(decimal startingCash);
    public IReadOnlyList<BacktestPositionSnapshot> Snapshots { get; }
    public IReadOnlyList<BacktestPositionCycle> CompletedCycles { get; }
    public LedgerApplication Apply(BacktestExecution execution);
}

public sealed record LedgerApplication(
    BacktestExecution Execution,
    BacktestPositionSnapshot Snapshot,
    BacktestPositionCycle? CompletedCycle);
```

매수는 현금·원가에서 `GrossAmount + Fee + Tax`를 반영하고 가중평균 평단을 계산한다. 매도는 변경 전에 초과 매도를 거부하며, 실현손익은 `GrossAmount - Fee - Tax - (평단 * 매도수량)`이다. 부분 매도 뒤 평단은 유지하고, 전량 매도 뒤 수량·평단·원가를 0으로 초기화하고 사이클을 하나 만든다. 계산에서는 반올림하지 않는다. 순번이 연속되지 않거나 체결 ID가 중복되면 변경 전 `ArgumentException`을 던진다.

- [ ] **통과를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~PositionLedgerTests --no-restore`

예상: 3개 테스트 통과.

- [ ] **이 작업의 다섯 파일만 커밋한다.**

`rtk git status --short`를 확인한 뒤 관련 없는 기존 변경은 제외한다.

커밋 메시지: `feat: add backtest position ledger`

### 작업 2: 러너 결과에 원장 연결

**파일:**

- 수정: `src/AssetPilot.Backtest/Execution/BacktestRunner.cs`
- 수정: `src/AssetPilot.Backtest/Execution/BacktestResult.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/BacktestRunnerTests.cs`

- [ ] **실패 회귀 테스트를 추가한다.**

```csharp
[Fact]
public void Run_assigns_ordered_executions_and_derives_a_closed_cycle()
{
    var result = Run(new[]
    {
        Bar("2026-01-02", 10_000m, 10_100m, 9_900m),
        Bar("2026-01-02", 10_000m, 10_600m, 9_900m),
    });

    Assert.Collection(result.Executions,
        buy => { Assert.Equal(1, buy.Sequence); Assert.Equal(ExecutionSide.Buy, buy.Side); },
        sell => { Assert.Equal(2, sell.Sequence); Assert.Equal(ExecutionSide.Sell, sell.Side); });
    Assert.Equal(2, result.PositionSnapshots.Count);
    Assert.Single(result.CompletedPositionCycles);
}
```

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunnerTests --no-restore`

- [ ] **기존 체결 판단을 바꾸지 않고 연결한다.**

러너의 직접 `executions.Add(...)`를 private 도우미로 바꾼다. 도우미가 `Guid.NewGuid()`, `executions.Count + 1`, `price * quantity`, 수수료·세금 0을 부여한다. 기존 시뮬레이션 완료 후 `PositionLedger(request.StartingCash)`에 체결을 순서대로 적용하고, 매도 체결은 원장이 산출한 `RealizedProfit`을 사용한다.

```csharp
public sealed record BacktestResult(
    IReadOnlyList<BacktestExecution> Executions,
    IReadOnlyList<BacktestDiagnostic> Diagnostics,
    IReadOnlyList<BacktestPositionSnapshot> PositionSnapshots,
    IReadOnlyList<BacktestPositionCycle> CompletedPositionCycles);
```

러너의 `cash`는 기존 배분수량과 종료 현금 계산에만 둔다. 실현손익의 원본은 `PositionLedger` 하나다.

- [ ] **회귀 테스트를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~BacktestRunnerTests|FullyQualifiedName~BacktestDiagnosticTests|FullyQualifiedName~SampleMarketDataIntegrationTests" --no-restore`

예상: 기존 익절·손절·보유기간 테스트 포함 전체 통과.

- [ ] **세 파일만 커밋한다.**

커밋 메시지: `feat: derive position records from backtest executions`

### 작업 3: 저장 실행 집계로 파생 결과 전달

**파일:**

- 수정: `src/AssetPilot.Backtest/Storage/BacktestRun.cs`
- 수정: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`
- 수정: `tests/AssetPilot.App.Tests/BacktestRunViewModelTests.cs`

- [ ] **저장 전파 실패 테스트를 작성한다.**

기존 `CapturingStore` 테스트에 다음을 추가한다.

```csharp
Assert.NotNull(store.SavedRun);
Assert.Equal(viewModel.Result!.PositionSnapshots, store.SavedRun.PositionSnapshots);
Assert.Equal(viewModel.Result.CompletedPositionCycles, store.SavedRun.CompletedPositionCycles);
```

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --filter FullyQualifiedName~BacktestRunViewModelTests --no-restore`

- [ ] **기존 생성자 호환성을 유지해 확장한다.**

`BacktestRun` 끝에 기존 `Diagnostics` 뒤로 선택적 컬렉션을 추가하고, 빈 컬렉션 접근자를 제공한다.

```csharp
IReadOnlyList<BacktestPositionSnapshot>? PositionSnapshots = null,
IReadOnlyList<BacktestPositionCycle>? CompletedPositionCycles = null)
{
    public IReadOnlyList<BacktestPositionSnapshot> PositionSnapshotsOrEmpty => PositionSnapshots ?? [];
    public IReadOnlyList<BacktestPositionCycle> CompletedPositionCyclesOrEmpty => CompletedPositionCycles ?? [];
}
```

`BacktestRunViewModel` 저장 시 두 결과 컬렉션을 전달한다. 화면 입력이나 표시 UI는 바꾸지 않는다.

- [ ] **앱·백테스트 테스트를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --filter FullyQualifiedName~BacktestRunViewModelTests --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

- [ ] **세 파일만 커밋한다.**

커밋 메시지: `feat: retain derived backtest position records`

### 작업 4: 기존 DB를 보존하는 SQLite 확장

**파일:**

- 수정: `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs`

- [ ] **저장·조회 실패 테스트를 작성한다.**

완전한 체결 2개, 스냅샷 2개, 사이클 1개를 가진 실행 결과를 저장·조회하고 체결 ID·순번·금액·실현손익, 스냅샷 현금·평단, 사이클 청산사유가 같은지 확인한다. 별도 테스트에서는 기존 세 테이블만 가진 DB를 직접 만든 뒤 `InitializeAsync` 후 새 형식 실행 결과가 저장·조회되는지도 확인한다.

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~SqliteBacktestRunStoreTests --no-restore`

- [ ] **추가형 마이그레이션만 구현한다.**

스냅샷·사이클 테이블을 `CREATE TABLE IF NOT EXISTS`로 추가한다. 기존 체결 테이블에는 아래 열만 추가하며, 중복 열 오류만 무시해 반복 초기화를 지원한다.

```sql
ALTER TABLE backtest_executions ADD COLUMN execution_id TEXT NULL;
ALTER TABLE backtest_executions ADD COLUMN execution_sequence INTEGER NULL;
ALTER TABLE backtest_executions ADD COLUMN gross_amount TEXT NULL;
ALTER TABLE backtest_executions ADD COLUMN fee TEXT NULL;
ALTER TABLE backtest_executions ADD COLUMN tax TEXT NULL;
```

새 실행은 체결·스냅샷·사이클을 같은 트랜잭션으로 저장한다. 이전 행의 새 열이 `NULL`이면 조회 시 `Guid.Empty`, 행 순서 기반 순번, `Price * Quantity`, 비용 0으로 복원한다. 체결 조회는 `execution_sequence IS NULL, execution_sequence, executed_at, rowid` 순서를 사용한다.

- [ ] **저장소 전체 테스트를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

- [ ] **두 파일만 커밋한다.**

커밋 메시지: `feat: persist backtest execution ledger`

### 작업 5: 저장된 실행 결과를 CSV로 내보내기

**파일:**

- 생성: `src/AssetPilot.Backtest/Storage/IBacktestRunExporter.cs`
- 생성: `src/AssetPilot.Infrastructure/Backtests/BacktestRunCsvExporter.cs`
- 생성: `tests/AssetPilot.Backtest.Tests/BacktestRunCsvExporterTests.cs`
- 수정: `src/AssetPilot.App/App.xaml.cs`

- [ ] **CSV 실패 테스트를 작성한다.**

임시 폴더와 저장된 실행 결과를 준비하여 `await exporter.ExportAsync(run.Id, outputDirectory);`를 호출한다. 아래 파일과 헤더를 검증한다.

```text
backtest_summary.csv: BacktestId,StrategyVersion,StartDate,EndDate,TakeProfitAtrMultiplier,StopLossAtrMultiplier,MaxHoldingDays,TotalReturn,CAGR,MDD,WinRate,TradeCount,ProfitFactor,AverageReturn
backtest_executions.csv: BacktestId,ExecutionId,ExecutionSequence,Symbol,DateTime,Side,Price,Quantity,GrossAmount,Fee,Tax,RealizedPnL,ExitReason,StrategyVersion
backtest_equity.csv: BacktestId,ExecutionId,DateTime,Cash,Quantity,AverageEntryPrice,PositionCost,RealizedProfitTotal
backtest_position_cycles.csv: BacktestId,CycleId,OpenedAt,ClosedAt,BoughtQuantity,SoldQuantity,AverageEntryPrice,AverageExitPrice,RealizedPnL,ReturnRate,ClosingReason
```

체결 CSV의 `Buy`·`Sell`이 별도 행으로 순서대로 있고, 시각은 ISO 8601 `O`, 소수점은 `.`, 파일은 UTF-8 BOM인지도 확인한다.

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunCsvExporterTests --no-restore`

- [ ] **계약과 내보내기를 구현한다.**

```csharp
public interface IBacktestRunExporter
{
    Task<BacktestCsvExport> ExportAsync(Guid backtestId, string outputDirectory, CancellationToken cancellationToken = default);
}

public sealed record BacktestCsvExport(
    string SummaryPath, string ExecutionsPath,
    string EquityPath, string PositionCyclesPath);
```

`BacktestRunCsvExporter`는 `IBacktestRunStore`에서 하나의 실행만 읽고 없으면 `KeyNotFoundException`을 던진다. 백테스트를 재실행하지 않는다. 출력 폴더를 만들고 네 파일만 덮어쓴다. UTF-8 BOM, `CultureInfo.InvariantCulture`, 쉼표·따옴표·줄바꿈 CSV 이스케이프를 사용한다. 아직 계산하지 않은 CAGR·MDD·승률·Profit Factor·평균수익률은 빈 칸으로 둔다. `TotalReturn`은 `BacktestRun.ReturnRate`를 쓰고, 옵션 JSON에서 세 청산값을 읽지 못하면 빈 칸으로 둔다. `App.xaml.cs`에는 singleton 등록만 하고 버튼은 추가하지 않는다.

- [ ] **내보내기·빌드를 검증한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunCsvExporterTests --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

- [ ] **네 파일만 커밋한다.**

커밋 메시지: `feat: export stored backtest ledger to csv`

### 작업 6: 통합 검증과 문서 상태 갱신

**파일:**

- 수정: `tests/AssetPilot.Backtest.Tests/SampleMarketDataIntegrationTests.cs`
- 수정: `docs/superpowers/specs/2026-11-16-backtest-execution-ledger-and-position-analysis.md`
- 수정: `docs/backtest-analysis-optimization.md`

- [ ] **샘플 데이터 회귀 테스트를 작성한다.**

고정 기준 전략으로 샘플 일봉·5분봉을 실행해 `PositionSnapshots.Count == Executions.Count`, 체결 순번이 `1..Count`, 완료 사이클의 종료 스냅샷 수량이 0임을 검증한다. 샘플 데이터는 바뀔 수 있으므로 고정 수익률·거래횟수는 검증하지 않는다.

- [ ] **통합 테스트를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~SampleMarketDataIntegrationTests --no-restore`

- [ ] **문서를 실제 구현 상태에 맞춘다.**

테스트가 통과한 경우에만 체결 원장, 스냅샷·사이클 SQLite 저장, CSV 4종을 완료로 표시한다. MFE/MAE, 시가평가 자산곡선, 성과지표, ATR 실험, Walk-Forward는 이후 단위로 남긴다.

- [ ] **전체 검증을 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

실행: `rtk git diff --check`

예상: 전체 테스트 통과, 앱 빌드 오류 없음, `git diff --check` 출력 없음.

- [ ] **문서 세 파일만 커밋한다.**

커밋 메시지: `docs: record backtest ledger and csv delivery`

## 계획 점검

- 체결·평단·부분매도·초과매도는 작업 1, 러너 연결은 작업 2, 실행 결과 전달은 작업 3, SQLite 호환 저장은 작업 4, CSV 계약은 작업 5, 샘플 데이터 회귀 검증은 작업 6이 담당한다.
- MFE/MAE, 실제 자산곡선·MDD, 성과지표, ATR 파라미터 실험, Walk-Forward, UI 표시는 의도적으로 다음 단계에 둔다.
- 이후 작업에서 쓰는 `ExecutionId`, `Sequence`, `BacktestPositionSnapshot`, `BacktestPositionCycle`, `PositionLedger`는 먼저 정의되는 작업에 포함되어 있다.
