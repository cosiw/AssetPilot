# 백테스트 시가평가 자산곡선 및 실제 MDD 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 5분봉 종가마다 시가평가한 자산곡선과 실제 MDD를 생성·저장·CSV 및 화면 요약으로 제공한다.

**Architecture:** 체결을 변경하지 않고 순수 `BacktestEquityCurveCalculator`가 체결 원장과 5분봉을 재생해 평가점을 만든다. 러너는 종료 후 곡선을 결과에 붙이고, SQLite는 평가점을 원본으로 저장한다. CSV와 UI는 저장되거나 실행 중 계산된 곡선만 읽으며 백테스트를 다시 실행하지 않는다.

**Tech Stack:** C# / .NET 10, xUnit, Microsoft.Data.Sqlite, WPF

---

## 파일 구조

| 파일 | 작업 |
| --- | --- |
| `src/AssetPilot.Backtest/Analysis/BacktestEquityPoint.cs` | 평가점·곡선 도메인 모델 |
| `src/AssetPilot.Backtest/Analysis/BacktestEquityCurveCalculator.cs` | 5분봉 종가 기준 곡선·MDD 계산 |
| `src/AssetPilot.Backtest/Execution/BacktestResult.cs` | 실행 결과에 곡선 전달 |
| `src/AssetPilot.Backtest/Execution/BacktestRunner.cs` | 러너 종료 뒤 곡선 계산 연결 |
| `src/AssetPilot.Backtest/Storage/BacktestRun.cs` | 저장 실행에 곡선 보관 |
| `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs` | `backtest_equity_points` 저장·조회 |
| `src/AssetPilot.Infrastructure/Backtests/BacktestRunCsvExporter.cs` | 실제 자산곡선 CSV와 summary MDD 출력 |
| `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs` | 실제 MDD 화면 요약 |
| `tests/AssetPilot.Backtest.Tests/BacktestEquityCurveCalculatorTests.cs` | 곡선·MDD 단위 테스트 |
| `tests/AssetPilot.Backtest.Tests/BacktestRunnerTests.cs` | 러너 분석 연결 회귀 |
| `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs` | 곡선 SQLite 왕복·이전 DB 호환 |
| `tests/AssetPilot.Backtest.Tests/BacktestRunCsvExporterTests.cs` | 자산곡선 CSV·summary MDD 테스트 |
| `tests/AssetPilot.App.Tests/BacktestRunViewModelTests.cs` | 화면 요약 실제 MDD 테스트 |

### 작업 1: 시가평가 곡선 계산기 (완료)

**파일:**

- 생성: `src/AssetPilot.Backtest/Analysis/BacktestEquityPoint.cs`
- 생성: `src/AssetPilot.Backtest/Analysis/BacktestEquityCurveCalculator.cs`
- 생성: `tests/AssetPilot.Backtest.Tests/BacktestEquityCurveCalculatorTests.cs`

- [x] **실패 테스트를 작성한다.**

```csharp
[Fact]
public void Calculate_marks_an_open_position_at_each_bar_close_and_finds_drawdown()
{
    var result = new BacktestEquityCurveCalculator().Calculate(
        1_000m,
        [Buy(1, "2026-01-02T09:00:00", 100m, 5m)],
        [Bar("2026-01-02T09:00:00", 100m), Bar("2026-01-02T09:05:00", 80m)]);

    Assert.Equal(2, result.Points.Count);
    Assert.Equal(900m, result.Points[1].TotalEquity);
    Assert.Equal(100m, result.MaxDrawdownAmount);
    Assert.Equal(0.10m, result.MaxDrawdownRate);
}
```

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestEquityCurveCalculatorTests --no-restore`

예상: 곡선 모델과 계산기가 없어 컴파일 실패.

- [x] **최소 구현을 추가한다.**

`Calculate(decimal startingCash, IReadOnlyList<BacktestExecution> executions, IReadOnlyList<FiveMinuteBar> bars)`를 구현한다. 각 봉에서 `ExecutedAt <= Timestamp`인 미반영 체결을 `Sequence` 순서로 적용하고, 봉 종가로 총자산을 기록한다. 최고 자산과 낙폭을 누적하며 최대 낙폭의 peak/trough 시점을 기록한다. 체결이 없는 봉도 평가점으로 기록한다.

- [x] **통과를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestEquityCurveCalculatorTests --no-restore`

- [ ] **커밋한다.**

커밋 메시지: `feat: calculate mark-to-market equity curve`

### 작업 2: 러너 결과에 곡선 연결 (완료)

**파일:**

- 수정: `src/AssetPilot.Backtest/Execution/BacktestResult.cs`
- 수정: `src/AssetPilot.Backtest/Execution/BacktestRunner.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/BacktestRunnerTests.cs`

- [x] **실패 회귀 테스트를 작성한다.**

기존 목표가 청산 러너 테스트에서 `result.EquityCurve.Points`가 5분봉 수와 같고, `result.EquityCurve.MaxDrawdownRate >= 0m`임을 검증한다.

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunnerTests --no-restore`

- [x] **연결한다.**

`BacktestResult` 마지막 선택 매개변수로 `BacktestEquityCurve? EquityCurve = null`을 추가한다. `BacktestRunner`는 ledger가 적용한 순서 있는 체결과 요청의 5분봉을 계산기에 전달한다. 체결 생성·손절 우선·보유기간 규칙은 수정하지 않는다.

- [x] **기존 백테스트 회귀를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~BacktestRunnerTests|FullyQualifiedName~BacktestDiagnosticTests|FullyQualifiedName~SampleMarketDataIntegrationTests" --no-restore`

- [ ] **커밋한다.**

커밋 메시지: `feat: attach equity curve to backtest result`

### 작업 3: SQLite 원본 저장과 조회 (완료)

**파일:**

- 수정: `src/AssetPilot.Backtest/Storage/BacktestRun.cs`
- 수정: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`
- 수정: `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs`

- [x] **실패 저장소 테스트를 작성한다.**

곡선 점 2개와 MDD 10%를 가진 `BacktestRun`을 저장·조회한 뒤 점별 현금·총자산·낙폭과 곡선 최대 낙폭이 모두 같은지 검증한다. 기존 테이블만 가진 DB를 초기화한 뒤 새 실행을 저장·조회하는 테스트도 유지한다.

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~SqliteBacktestRunStoreTests --no-restore`

- [x] **추가형 저장을 구현한다.**

`backtest_equity_points` 테이블을 `CREATE TABLE IF NOT EXISTS`로 추가한다. `BacktestRun` 마지막 선택 매개변수에 `EquityCurve`를 추가한다. 저장소는 점별 값을 invariant culture로 저장하고 `recorded_at` 순서로 읽는다. 조회한 점 목록에서 최대 낙폭과 peak/trough를 재구성한다. 기존 실행은 빈 곡선과 MDD 0으로 반환한다.

- [x] **저장소 전체 테스트를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

- [ ] **커밋한다.**

커밋 메시지: `feat: persist mark-to-market equity curve`

### 작업 4: CSV와 화면 실제 MDD 연결 (완료)

**파일:**

- 수정: `src/AssetPilot.Infrastructure/Backtests/BacktestRunCsvExporter.cs`
- 수정: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/BacktestRunCsvExporterTests.cs`
- 수정: `tests/AssetPilot.App.Tests/BacktestRunViewModelTests.cs`

- [x] **실패 CSV·화면 테스트를 작성한다.**

`backtest_equity.csv` 헤더가 `BacktestId,DateTime,Cash,PositionQuantity,AverageEntryPrice,MarkPrice,PositionValue,TotalEquity,CumulativeReturn,PeakEquity,DrawdownAmount,DrawdownRate`인지 확인한다. summary의 MDD가 `0.1`이고, 화면 요약에 `MDD 10.00%`가 표시되며 `실현손익 기준` 문구가 사라졌는지 확인한다.

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunCsvExporterTests --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --filter FullyQualifiedName~BacktestRunViewModelTests --no-restore`

- [x] **출력과 요약을 구현한다.**

기존 `backtest_equity.csv`의 snapshot 출력은 제거하고 `EquityCurve.Points`를 출력한다. summary의 `MDD` 열에는 `EquityCurve.MaxDrawdownRate`를 쓴다. `MetricsSummary`는 실제 MDD 비율을 보고하고 legacy `BacktestMetrics.MaxDrawdown`을 표시하지 않는다.

- [x] **CSV·앱 테스트와 빌드를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

- [ ] **커밋한다.**

커밋 메시지: `feat: export actual backtest drawdown`

### 작업 5: 샘플 회귀와 문서 상태 갱신 (완료)

**파일:**

- 수정: `tests/AssetPilot.Backtest.Tests/SampleMarketDataIntegrationTests.cs`
- 수정: `docs/backtest-analysis-optimization.md`
- 수정: `docs/superpowers/specs/2026-11-17-backtest-mark-to-market-equity-and-mdd.md`

- [x] **샘플 데이터 회귀를 추가한다.**

KODEX 200 일봉과 `5분봉(26년).csv` 실행에서 자산곡선 점 수가 입력 5분봉 수와 같고, 모든 점의 `TotalEquity`가 0 이상이며, `MaxDrawdownRate`가 0 이상임을 검증한다.

- [x] **전체 검증을 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

실행: `rtk git -c safe.directory=C:/Dev/AssetPilot diff --check`

- [ ] **커밋한다.**

커밋 메시지: `docs: record actual mdd delivery`
