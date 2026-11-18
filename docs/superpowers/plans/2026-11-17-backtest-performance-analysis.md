# 백테스트 성과 분석 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 완료 포지션 사이클과 5분봉으로 재현 가능한 성과 보고서, 청산 사유별 성과, 월별 성과, MFE/MAE를 계산·저장·내보낸다.

**Architecture:** 체결을 바꾸지 않고, 러너 종료 후 순수 `BacktestExcursionCalculator`가 사이클별 excursion을 계산한다. 순수 `BacktestPerformanceCalculator`는 완료 사이클과 분석 기간을 받아 보고서를 계산한다. 저장소와 CSV는 이 계산 결과를 보관·출력할 뿐 다시 백테스트하지 않는다.

**Tech Stack:** C# / .NET 10, xUnit, Microsoft.Data.Sqlite, WPF

---

## 파일 구조

| 파일 | 작업 |
| --- | --- |
| `src/AssetPilot.Backtest/Analysis/BacktestAnalysisOptions.cs` | 고정 관찰 거래일 설정 |
| `src/AssetPilot.Backtest/Analysis/BacktestExcursion.cs` | 사이클별 MFE/MAE 결과 |
| `src/AssetPilot.Backtest/Analysis/BacktestExcursionCalculator.cs` | 5분봉 기반 excursion 계산 |
| `src/AssetPilot.Backtest/Analysis/BacktestPerformanceReport.cs` | 성과·청산사유·월별 결과 모델 |
| `src/AssetPilot.Backtest/Analysis/BacktestPerformanceCalculator.cs` | 순수 성과 보고서 계산 |
| `src/AssetPilot.Backtest/Execution/BacktestResult.cs` | excursion·성과 보고서 전달 |
| `src/AssetPilot.Backtest/Execution/BacktestRunner.cs` | 러너 종료 뒤 두 계산기 호출 |
| `src/AssetPilot.Backtest/Storage/BacktestRun.cs` | 분석 기간·옵션·결과 저장 |
| `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs` | 추가형 테이블·열 저장 및 조회 |
| `src/AssetPilot.Infrastructure/Backtests/BacktestRunCsvExporter.cs` | 요약 및 분석 CSV 출력 |
| `tests/AssetPilot.Backtest.Tests/BacktestExcursionCalculatorTests.cs` | MFE/MAE 경계 테스트 |
| `tests/AssetPilot.Backtest.Tests/BacktestPerformanceCalculatorTests.cs` | CAGR·사유·월별 성과 테스트 |
| `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs` | 분석 데이터 SQLite 왕복 테스트 |
| `tests/AssetPilot.Backtest.Tests/BacktestRunCsvExporterTests.cs` | CSV 확장 테스트 |

### 작업 1: 포지션 사이클 기준 성과 계산기

**파일:**

- 생성: `src/AssetPilot.Backtest/Analysis/BacktestPerformanceReport.cs`
- 생성: `src/AssetPilot.Backtest/Analysis/BacktestPerformanceCalculator.cs`
- 생성: `tests/AssetPilot.Backtest.Tests/BacktestPerformanceCalculatorTests.cs`

- [x] **실패 테스트를 작성한다.**

```csharp
[Fact]
public void Calculate_returns_cagr_exit_reason_and_monthly_performance()
{
    var cycles = new[]
    {
        Cycle("2026-01-10", 100m, 0.10m, ExitReason.Target),
        Cycle("2026-02-10", -50m, -0.05m, ExitReason.Stop),
        Cycle("2026-02-20", 30m, 0.03m, ExitReason.Time),
    };

    var report = BacktestPerformanceCalculator.Calculate(1_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 1), cycles, []);

    Assert.Equal(3, report.CompletedTrades);
    Assert.Equal(80m, report.TotalProfit);
    Assert.Equal(0.08m, report.TotalReturn);
    Assert.True(report.Cagr > 0m);
    Assert.Equal(3, report.ExitReasonPerformances.Count);
    Assert.Equal(2, report.MonthlyPerformances.Count);
}
```

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestPerformanceCalculatorTests --no-restore`

예상: 성과 보고서와 계산기가 없어 컴파일 실패.

- [x] **최소 구현을 추가한다.**

`Calculate`은 시작자금, 데이터 시작일·종료일, 완료 사이클, excursions를 입력으로 받아 명세의 공식대로 `BacktestPerformanceReport`를 반환한다. 사이클은 `ClosedAt` 순서로 정렬한다. 월별 성과는 `ClosedAt`의 `DateOnly(연, 월, 1)`로 묶는다. 보고서에 `Excursions`를 그대로 포함한다. 기간이 0일이면 CAGR은 0으로 둔다.

- [x] **통과를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestPerformanceCalculatorTests --no-restore`

- [ ] **세 파일만 커밋한다.**

커밋 메시지: `feat: calculate cycle-based backtest performance`

### 작업 2: 미래 정보 없는 MFE/MAE 계산기

**파일:**

- 생성: `src/AssetPilot.Backtest/Analysis/BacktestAnalysisOptions.cs`
- 생성: `src/AssetPilot.Backtest/Analysis/BacktestExcursion.cs`
- 생성: `src/AssetPilot.Backtest/Analysis/BacktestExcursionCalculator.cs`
- 생성: `tests/AssetPilot.Backtest.Tests/BacktestExcursionCalculatorTests.cs`

- [x] **실패 테스트를 작성한다.**

```csharp
[Fact]
public void Calculate_excludes_entry_and_exit_bars_but_includes_actual_fill_prices()
{
    var cycle = Cycle(entry: 100m, exit: 105m, openedAt: "2026-01-02T09:00:00", closedAt: "2026-01-02T09:10:00");
    var bars = new[]
    {
        Bar("2026-01-02T09:00:00", high: 130m, low: 70m),
        Bar("2026-01-02T09:05:00", high: 110m, low: 90m),
        Bar("2026-01-02T09:10:00", high: 140m, low: 60m),
    };

    var result = Assert.Single(new BacktestExcursionCalculator().Calculate(bars, new[] { cycle }, new BacktestAnalysisOptions(1)));

    Assert.Equal(10m, result.HoldingMfeAmount);
    Assert.Equal(-10m, result.HoldingMaeAmount);
}
```

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestExcursionCalculatorTests --no-restore`

예상: 분석 옵션, excursion 모델, 계산기가 없어 컴파일 실패.

- [x] **최소 구현을 추가한다.**

`Calculate(IReadOnlyList<FiveMinuteBar> bars, IReadOnlyList<BacktestPositionCycle> cycles, BacktestAnalysisOptions options)`를 만든다. 각 사이클의 opening·closing execution 가격은 `BacktestExecution` 목록에서 조회할 수 있도록 계산기 입력에 `IReadOnlyList<BacktestExecution>`도 추가한다. 진입·청산 봉 사이의 고가·저가를 제외하고, 진입가·청산가·사이 봉 OHLC만 후보로 사용한다. 고정기간은 진입일 포함 거래일 수로 계산한다. 사후 excursion은 청산 직후 다음 봉부터 고정기간 종료까지 청산가를 기준으로 계산하고, 이용 가능한 봉이 없으면 null이다.

- [x] **통과를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestExcursionCalculatorTests --no-restore`

- [ ] **네 파일만 커밋한다.**

커밋 메시지: `feat: calculate backtest mfe and mae`

### 작업 3: 러너와 실행 결과에 분석 연결

**파일:**

- 수정: `src/AssetPilot.Backtest/Execution/BacktestResult.cs`
- 수정: `src/AssetPilot.Backtest/Execution/BacktestRunner.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/BacktestRunnerTests.cs`

- [x] **실패 회귀 테스트를 작성한다.**

기존 목표가 청산 러너 테스트에서 `result.PerformanceReport.CompletedTrades == 1`, `result.Excursions.Count == 1`, `result.Excursions[0].CycleId == result.CompletedPositionCycles[0].CycleId`를 검증한다.

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunnerTests --no-restore`

- [x] **연결한다.**

`BacktestRequest`에 `BacktestAnalysisOptions? AnalysisOptions = null`을 마지막 선택 매개변수로 추가한다. 러너 종료 뒤 `BacktestExcursionCalculator`와 `BacktestPerformanceCalculator`를 호출한다. 데이터 시작·종료일은 정렬된 일봉의 첫·마지막 날짜를 사용한다. 기존 체결 판단, 수량 계산, 손절 우선 규칙은 변경하지 않는다.

- [x] **기존 백테스트 회귀를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~BacktestRunnerTests|FullyQualifiedName~BacktestDiagnosticTests|FullyQualifiedName~SampleMarketDataIntegrationTests" --no-restore`

- [ ] **세 파일만 커밋한다.**

커밋 메시지: `feat: attach performance analysis to backtest results`

### 작업 4: SQLite에 분석 입력과 결과 저장

**파일:**

- 수정: `src/AssetPilot.Backtest/Storage/BacktestRun.cs`
- 수정: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`
- 수정: `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs`

- [x] **실패 저장소 테스트를 작성한다.**

분석 옵션, 데이터 기간, Target·Stop·Time 사유별 보고서, excursion 하나를 가진 `BacktestRun`을 저장·조회한 뒤 CAGR·고정 관찰일·MFE·사후 MFE가 같음을 검증한다. 기존 세 테이블만 가진 DB의 초기화·저장·조회 회귀 테스트도 유지한다.

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~SqliteBacktestRunStoreTests --no-restore`

- [x] **추가형 저장을 구현한다.**

`backtest_runs`에 nullable `data_start_date`, `data_end_date`, `analysis_options_json` 열을 추가한다. `backtest_excursions` 테이블을 추가한다. 보고서는 조회한 cycles·excursions와 저장된 기간·시작자금에서 재계산하여 중복 저장하지 않는다. 이전 실행은 기간이 없으므로 CAGR 0과 빈 excursion으로 반환한다.

- [x] **저장소 전체 테스트를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

- [ ] **네 파일만 커밋한다.**

커밋 메시지: `feat: persist backtest performance analysis`

### 작업 5: CSV와 화면 요약 연결

**파일:**

- 수정: `src/AssetPilot.Infrastructure/Backtests/BacktestRunCsvExporter.cs`
- 수정: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/BacktestRunCsvExporterTests.cs`
- 수정: `tests/AssetPilot.App.Tests/BacktestRunViewModelTests.cs`

- [x] **실패 CSV 테스트를 작성한다.**

`backtest_summary.csv` 행에 CAGR, WinRate, TradeCount, ProfitFactor, AverageReturn이 채워졌는지 확인한다. `backtest_position_cycles.csv` 헤더와 행에 HoldingMfeRate, HoldingMaeRate, FixedMfeRate, FixedMaeRate, PostExitMfeRate, PostExitMaeRate가 포함되는지 확인한다. `backtest_exit_reason_performance.csv`와 `backtest_monthly_performance.csv`가 생성되는지 확인한다.

- [x] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunCsvExporterTests --no-restore`

- [x] **출력과 요약을 구현한다.**

기존 CSV 파일은 유지하며 새 두 CSV를 `BacktestCsvExport`에 추가한다. `MetricsSummary`는 기존 지표 대신 새 보고서의 완료 거래수·승률·PF·CAGR·최대 연속 손실을 표시한다. MDD는 4번 전까지 화면에서 `실현손익 기준 낙폭`이라고 명시한다.

- [x] **CSV·앱 테스트와 빌드를 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestRunCsvExporterTests --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --filter FullyQualifiedName~BacktestRunViewModelTests --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

- [ ] **네 파일만 커밋한다.**

커밋 메시지: `feat: export backtest performance analysis`

### 작업 6: 전체 회귀와 문서 상태 갱신

**파일:**

- 수정: `docs/backtest-analysis-optimization.md`
- 수정: `docs/superpowers/specs/2026-11-17-backtest-performance-analysis.md`

- [x] **샘플 데이터 분석 회귀를 추가한다.**

샘플 CSV 백테스트에서 완료 사이클 수와 보고서 거래 수가 같고, 각 excursion이 하나의 완료 사이클에만 연결되며, 고정 관찰일이 5로 기록되는지 확인한다.

- [x] **전체 검증을 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

실행: `rtk git -c safe.directory=C:/Dev/AssetPilot diff --check`

예상: 전체 테스트 통과, 앱 빌드 오류 없음, 공백 오류 없음.

- [ ] **문서 두 파일만 커밋한다.**

커밋 메시지: `docs: record backtest performance analysis delivery`

## 계획 점검

- 사이클 기준 성과는 작업 1, MFE/MAE 안전 규칙은 작업 2, 러너 연결은 작업 3, 재현 가능한 저장은 작업 4, CSV와 화면 출력은 작업 5, 회귀와 문서 갱신은 작업 6이 담당한다.
- 시가평가 자산곡선·진짜 MDD와 ATR 조합 실험은 이 계획의 범위 밖이다.
