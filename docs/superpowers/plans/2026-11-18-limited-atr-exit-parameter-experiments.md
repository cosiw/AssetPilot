# 제한적 ATR 청산 파라미터 실험 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**목표:** 기준 진입 전략을 고정한 채 3·9·27개의 청산 파라미터 후보를 동일한 백테스트 조건에서 실행·저장·CSV 비교한다.

**아키텍처:** `AssetPilot.Backtest.Experiments`는 유효한 후보 목록과 후보별 기존 `BacktestRunner` 실행 결과를 만든다. `AssetPilot.App`의 응용 서비스는 각 결과를 기존 `IBacktestRunStore`에 저장하고 하나의 실험 요약 CSV를 만든다. 화면은 수치 표와 CSV 경로만 보여 주며 추천·승인·자동 적용을 하지 않는다.

**기술:** C# / .NET 10, xUnit, WPF, SQLite, 기존 `BacktestRunner`, `BacktestRunCsvExporter`

---

## 파일 구조

| 파일 | 책임 |
| --- | --- |
| `src/AssetPilot.Backtest/Experiments/ExitParameterExperimentScope.cs` | 3·9·27 실험 단계 열거형 |
| `src/AssetPilot.Backtest/Experiments/ExitParameterCandidate.cs` | 불변 청산 후보 값과 유효성 검사 |
| `src/AssetPilot.Backtest/Experiments/ExitParameterCandidateFactory.cs` | 단계별 고유 후보 생성 |
| `src/AssetPilot.Backtest/Experiments/BacktestExperiment.cs` | 후보 하나의 백테스트 결과와 비교 지표 |
| `src/AssetPilot.Backtest/Experiments/BacktestExperimentRunner.cs` | 기존 러너를 재사용한 동일 조건의 후보 실행 |
| `src/AssetPilot.Backtest/Experiments/BacktestExperimentReport.cs` | 실행 단위·실패 후보·저장 대상 결과 묶음 |
| `src/AssetPilot.Infrastructure/Backtests/BacktestExperimentCsvExporter.cs` | 실험 요약 CSV 출력 전담 |
| `src/AssetPilot.App/ViewModels/BacktestExperimentViewModel.cs` | 수동 실험 실행, 상태·결과 표·CSV 경로만 제공 |
| `src/AssetPilot.App/BacktestExperimentWindow.xaml` | 실험 단계 선택·실행·결과 표 UI |
| `src/AssetPilot.App/App.xaml.cs` | 실험 실행기·내보내기·ViewModel/Window DI 조립 |
| `src/AssetPilot.MarketDataWorker/MarketDataRunner.cs` | 월말 자동 추천 호출 제거 |
| `src/AssetPilot.MarketDataWorker/Program.cs` | 워커의 자동 추천·결정 저장소 등록 제거 |

## 작업 1: 자동 추천 경로를 실험 기능과 분리

**파일:**

- 수정: `src/AssetPilot.MarketDataWorker/MarketDataRunner.cs`
- 수정: `src/AssetPilot.MarketDataWorker/Program.cs`
- 수정: `docs/backtest-analysis-optimization.md`

- [ ] **자동 추천 호출의 현재 동작을 확인한다.**

`MarketDataRunner.RunAsync`의 월말 분기에서 `WalkForwardParameterSelector.Select`와 `ParameterDecisionStore.SavePendingAsync`가 호출되는지 확인한다. 해당 호출은 데이터 수집 이후의 추천 생성일 뿐 실험 실행기가 아니다.

- [ ] **월말 추천 의존성을 제거한다.**

`MarketDataRunner` 생성자에서 `WalkForwardParameterSelector selector`, `ParameterDecisionStore decisions`를 제거하고 월말 분기와 `IsLastWeekdayOfMonth` 메서드를 삭제한다. 워커 `Program`에서는 더 이상 사용하지 않는 두 서비스 등록을 제거한다. 앱의 기존 승인 이력 조회 등록은 보존한다. 시장 데이터 수집·CSV 병합·스케줄 적격성 판정은 변경하지 않는다.

- [ ] **작업자 프로젝트를 빌드한다.**

실행: `rtk dotnet build .\src\AssetPilot.MarketDataWorker\AssetPilot.MarketDataWorker.csproj --no-restore`

예상: 오류 0. `WalkForwardParameterSelector`, 승인 기록 클래스와 기존 기록 파일은 삭제하지 않는다.

- [ ] **커밋한다.**

커밋 메시지: `refactor: stop automatic parameter recommendations`

## 작업 2: 단계별 청산 후보를 테스트 우선으로 생성

**파일:**

- 생성: `src/AssetPilot.Backtest/Experiments/ExitParameterExperimentScope.cs`
- 생성: `src/AssetPilot.Backtest/Experiments/ExitParameterCandidate.cs`
- 생성: `src/AssetPilot.Backtest/Experiments/ExitParameterCandidateFactory.cs`
- 생성: `tests/AssetPilot.Backtest.Tests/ExitParameterCandidateFactoryTests.cs`

- [ ] **실패 테스트를 작성한다.**

```csharp
[Theory]
[InlineData(ExitParameterExperimentScope.TakeProfitOnly, 3)]
[InlineData(ExitParameterExperimentScope.TakeProfitAndStopLoss, 9)]
[InlineData(ExitParameterExperimentScope.AllExitParameters, 27)]
public void Create_returns_the_expected_number_of_unique_candidates(
    ExitParameterExperimentScope scope, int expectedCount)
{
    var candidates = new ExitParameterCandidateFactory().Create(scope);

    Assert.Equal(expectedCount, candidates.Count);
    Assert.Equal(expectedCount, candidates.Distinct().Count());
    Assert.All(candidates, candidate =>
    {
        Assert.True(candidate.TakeProfitAtrMultiplier > 0m);
        Assert.True(candidate.StopLossAtrMultiplier > 0m);
        Assert.True(candidate.MaxHoldingDays >= 1);
    });
}
```

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~ExitParameterCandidateFactoryTests --no-restore`

예상: 후보 형식과 생성기가 없어 컴파일 실패.

- [ ] **최소 구현을 추가한다.**

```csharp
public enum ExitParameterExperimentScope
{
    TakeProfitOnly,
    TakeProfitAndStopLoss,
    AllExitParameters
}

public sealed record ExitParameterCandidate(
    decimal TakeProfitAtrMultiplier,
    decimal StopLossAtrMultiplier,
    int MaxHoldingDays)
{
    public ExitStrategyOptions ToExitOptions() =>
        new(TakeProfitAtrMultiplier, StopLossAtrMultiplier, MaxHoldingDays);
}
```

`ExitParameterCandidateFactory.Create`는 익절 `[1m, 1.5m, 2m]`, 손절 `[.8m, 1m, 1.2m]`, 보유일 `[3, 5, 7]`의 데카르트 곱을 만들되 선택 단계에서 고정값 `1m`, `5`를 사용한다. 정렬은 익절·손절·보유일 오름차순으로 고정한다.

- [ ] **통과를 확인한다.**

실행: 위 테스트 명령

예상: 3개 테스트 통과.

- [ ] **커밋한다.**

커밋 메시지: `feat: define ATR exit experiment candidates`

## 작업 3: 기존 러너를 재사용하는 후보 실행기

**파일:**

- 생성: `src/AssetPilot.Backtest/Experiments/BacktestExperiment.cs`
- 생성: `src/AssetPilot.Backtest/Experiments/BacktestExperimentReport.cs`
- 생성: `src/AssetPilot.Backtest/Experiments/BacktestExperimentRunner.cs`
- 생성: `tests/AssetPilot.Backtest.Tests/BacktestExperimentRunnerTests.cs`

- [ ] **실패 테스트를 작성한다.**

```csharp
[Fact]
public void Run_reuses_the_fixed_baseline_entry_options_for_every_candidate()
{
    var report = new BacktestExperimentRunner().Run(
        CreateRequest(),
        ExitParameterExperimentScope.TakeProfitOnly);

    Assert.Equal(3, report.Results.Count);
    Assert.All(report.Results, result =>
    {
        Assert.Equal(120, result.StrategyOptions.SmaPeriod);
        Assert.Equal(5, result.StrategyOptions.ShortSmaPeriod);
        Assert.Equal(14, result.StrategyOptions.AtrPeriod);
        Assert.Equal(5, result.StrategyOptions.SmaSlopeLookbackDays);
        Assert.Equal(1m, result.StrategyOptions.MinimumSmaDistancePercent);
        Assert.Equal(0.5m, result.StrategyOptions.BuyBandAtrMultiplier);
    });
}
```

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter FullyQualifiedName~BacktestExperimentRunnerTests --no-restore`

예상: 실험 실행기와 결과 형식이 없어 컴파일 실패.

- [ ] **실행기를 구현한다.**

`BacktestExperimentRunner.Run(BacktestRequest sourceRequest, ExitParameterExperimentScope scope)`는 새 `Guid` 실험 ID와 후보 목록을 만든다. 각 후보에서 다음을 실행한다.

```csharp
var options = BaselineStrategyOptions.Create(candidate.ToExitOptions());
var request = sourceRequest with { MaxHoldingDays = candidate.MaxHoldingDays };
var result = new BacktestRunner().Run(request, new SwingAtrStrategy(options));
```

후보별 `BacktestExperiment`에는 후보, 생성된 `StrategyOptions`, `BacktestResult`, 계산된 `BacktestMetrics`, `BacktestPerformanceReport?`, 실제 `BacktestEquityCurve?`, 오류 문자열을 담는다. 한 후보 실행이 예외를 던지면 오류 결과를 추가하고 다음 후보를 계속 실행한다. 일봉 또는 5분봉 입력이 비어 있으면 후보를 실행하지 않고 `ArgumentException`을 던진다.

- [ ] **통과와 기존 회귀를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~BacktestExperimentRunnerTests|FullyQualifiedName~BacktestRunnerTests|FullyQualifiedName~BaselineStrategyOptionsTests" --no-restore`

- [ ] **커밋한다.**

커밋 메시지: `feat: run fixed-entry ATR exit experiments`

## 작업 4: 저장된 후보 결과를 실험 요약 CSV로 출력

**파일:**

- 생성: `src/AssetPilot.Infrastructure/Backtests/BacktestExperimentCsvExporter.cs`
- 생성: `tests/AssetPilot.Backtest.Tests/BacktestExperimentCsvExporterTests.cs`
- 수정: `src/AssetPilot.Backtest/Storage/BacktestRun.cs`
- 수정: `src/AssetPilot.Infrastructure/Backtests/SqliteBacktestRunStore.cs`
- 수정: `tests/AssetPilot.Backtest.Tests/SqliteBacktestRunStoreTests.cs`

- [ ] **실패 저장·CSV 테스트를 작성한다.**

```csharp
[Fact]
public async Task ExportAsync_writes_one_row_per_saved_candidate_with_actual_mdd()
{
    var output = await exporter.ExportAsync(experiment, outputDirectory);
    var lines = await File.ReadAllLinesAsync(output);

    Assert.Equal("ExperimentId,BacktestId,StrategyVersion,StartDate,EndDate,TakeProfitAtrMultiplier,StopLossAtrMultiplier,MaxHoldingDays,TotalReturn,CAGR,MDD,WinRate,TradeCount,ProfitFactor,AverageReturn,AverageWin,AverageLoss,MaxConsecutiveLosses,Error", lines[0]);
    Assert.Equal(4, lines.Length); // header + 3 candidates
    Assert.Contains(",0.1,", lines[1]);
}
```

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --filter "FullyQualifiedName~BacktestExperimentCsvExporterTests|FullyQualifiedName~SqliteBacktestRunStoreTests" --no-restore`

- [ ] **저장과 내보내기를 구현한다.**

`BacktestRun`의 마지막 선택 매개변수에 `Guid? ExperimentId = null`을 추가하고 `backtest_runs`에 nullable `experiment_id` 열을 마이그레이션한다. 실험 앱 서비스는 성공 후보마다 개별 `BacktestRun`을 저장하고 그 ID를 결과에 붙인다. `BacktestExperimentCsvExporter`는 실행 중 보유한 결과와 저장된 run ID를 받아 위 헤더의 행을 invariant culture와 UTF-8 BOM으로 쓴다. MDD는 `BacktestResult.EquityCurve.MaxDrawdownRate`, 실패 후보 수치 열은 빈 문자열, 마지막 `Error` 열은 오류 메시지다.

- [ ] **통과를 확인한다.**

실행: 위 테스트 명령

- [ ] **커밋한다.**

커밋 메시지: `feat: export ATR exit experiment summaries`

## 작업 5: 수동 WPF 실행 화면과 저장 조립

**파일:**

- 생성: `src/AssetPilot.App/ViewModels/BacktestExperimentViewModel.cs`
- 생성: `src/AssetPilot.App/BacktestExperimentWindow.xaml`
- 생성: `src/AssetPilot.App/BacktestExperimentWindow.xaml.cs`
- 수정: `src/AssetPilot.App/App.xaml.cs`
- 수정: `src/AssetPilot.App/BacktestInputWindow.xaml`
- 수정: `src/AssetPilot.App/BacktestInputWindow.xaml.cs`
- 생성: `tests/AssetPilot.App.Tests/BacktestExperimentViewModelTests.cs`
- 수정: `tests/AssetPilot.App.Tests/BacktestWindowLayoutTests.cs`

- [ ] **실패 ViewModel 테스트를 작성한다.**

```csharp
[Fact]
public async Task RunAsync_runs_three_take_profit_candidates_without_changing_active_strategy()
{
    var viewModel = CreateViewModelWithSampleCsv();

    await viewModel.RunCommand.ExecuteAsync(ExitParameterExperimentScope.TakeProfitOnly);

    Assert.Equal(3, viewModel.Results.Count);
    Assert.All(viewModel.Results, result => Assert.Equal(5, result.Candidate.MaxHoldingDays));
    Assert.DoesNotContain("추천", viewModel.StatusMessage);
    Assert.DoesNotContain("적용", viewModel.StatusMessage);
}
```

- [ ] **실패를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --filter FullyQualifiedName~BacktestExperimentViewModelTests --no-restore`

- [ ] **수동 실행 UI를 구현한다.**

ViewModel은 일봉·5분봉 CSV 경로, 시작자금, 투자 비율, 출력 폴더를 받고 `TakeProfitOnly`, `TakeProfitAndStopLoss`, `AllExitParameters` 명령을 제공한다. 실행 중에는 세 명령을 비활성화하고, 완료 후 후보·CAGR·MDD·PF·거래 수·오류를 `ObservableCollection`에 표시한다. 성공 후보는 기존 SQLite 저장소에 저장하고 요약 CSV 경로를 표시한다. `BacktestInputWindow`에는 실험 창을 여는 버튼만 추가한다. UI는 후보 순위를 자동 선정하거나 기준 전략 값을 변경하지 않는다.

- [ ] **앱 테스트와 빌드를 확인한다.**

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

- [ ] **커밋한다.**

커밋 메시지: `feat: run ATR exit experiments manually`

## 작업 6: 샘플 회귀와 운영 문서 갱신

**파일:**

- 수정: `tests/AssetPilot.Backtest.Tests/SampleMarketDataIntegrationTests.cs`
- 수정: `docs/backtest-analysis-optimization.md`
- 수정: `docs/superpowers/specs/2026-11-18-limited-atr-exit-parameter-experiments.md`

- [ ] **샘플 회귀를 추가한다.**

KODEX 200 일봉과 `5분봉(26년).csv`로 3개 익절 후보를 실행하고 후보 수가 3개이며, 후보마다 고정 진입 옵션·5분봉 자산곡선·실제 MDD가 있는지 검증한다. 수익률의 특정 숫자를 고정하지 않는다.

- [ ] **전체 검증을 실행한다.**

실행: `rtk dotnet test .\tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore`

실행: `rtk dotnet test .\tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.App\AssetPilot.App.csproj --no-restore`

실행: `rtk dotnet build .\src\AssetPilot.MarketDataWorker\AssetPilot.MarketDataWorker.csproj --no-restore`

실행: `rtk git -c safe.directory=C:/Dev/AssetPilot diff --check`

- [ ] **커밋한다.**

커밋 메시지: `docs: record ATR exit experiment delivery`

> 구현 상태 (2026-11-18): 작업 1~6 완료. 커밋은 수행하지 않음.
