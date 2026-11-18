# Single ATR Backtest Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WPF에서 ATR 단일 진입 전략의 입력값을 명확히 편집하고, 실행 뒤에는 결과와 실행 당시 설정을 읽기 전용으로 확인한다.

**Architecture:** `BacktestRunViewModel`이 단일 전략의 다섯 파라미터를 소유하고 유효성을 확인한 뒤 `StrategyOptions`를 만들어 실행한다. `BacktestWindow`는 실행 전 입력 패널과 실행 후 결과 패널을 한 창에 표시하며, 실행 설정은 결과에서 수정하지 않는다.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit

---

### Task 1: 전략 설정 ViewModel 동작

**Files:**
- Modify: `tests/AssetPilot.App.Tests/BacktestRunViewModelTests.cs`
- Modify: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`

- [ ] **Step 1: 실패하는 검증 테스트를 작성한다.**

```csharp
[Fact]
public void Run_rejects_non_positive_atr_period()
{
    var viewModel = new BacktestRunViewModel { AtrPeriod = 0 };
    viewModel.RunCommand.Execute(null);
    Assert.Equal("ATR 기간은 1 이상이어야 합니다.", viewModel.StatusMessage);
}
```

- [ ] **Step 2: 테스트가 새 속성 또는 검증 메시지 부재로 실패하는지 확인한다.**

Run: `rtk dotnet test tests\AssetPilot.App.Tests --filter FullyQualifiedName~BacktestRunViewModelTests`

- [ ] **Step 3: 다섯 전략 속성, 검증, StrategyOptions 변환을 구현한다.**

```csharp
[ObservableProperty] public partial int SmaPeriod { get; set; } = 120;
[ObservableProperty] public partial int AtrPeriod { get; set; } = 14;
[ObservableProperty] public partial decimal BuyBandAtrMultiplier { get; set; } = 0.5m;
[ObservableProperty] public partial decimal TargetAtrMultiplier { get; set; } = 1.5m;
[ObservableProperty] public partial decimal StopAtrMultiplier { get; set; } = 1.0m;
```

- [ ] **Step 4: 단위 테스트를 실행해 통과를 확인한다.**

Run: `rtk dotnet test tests\AssetPilot.App.Tests --filter FullyQualifiedName~BacktestRunViewModelTests`

### Task 2: 입력과 결과를 분리한 WPF 레이아웃

**Files:**
- Modify: `tests/AssetPilot.App.Tests/BacktestWindowLayoutTests.cs`
- Modify: `src/AssetPilot.App/BacktestWindow.xaml`

- [ ] **Step 1: 다섯 전략 바인딩과 읽기 전용 실행 설정을 기대하는 실패 테스트를 작성한다.**

```csharp
Assert.Contains("SmaPeriod", xaml);
Assert.Contains("BuyBandAtrMultiplier", xaml);
Assert.Contains("실행 당시 설정", xaml);
```

- [ ] **Step 2: 테스트가 레이아웃 마커 부재로 실패하는지 확인한다.**

Run: `rtk dotnet test tests\AssetPilot.App.Tests --filter FullyQualifiedName~BacktestWindowLayoutTests`

- [ ] **Step 3: 데이터 파일·자금·다섯 전략 값을 설명과 함께 입력하는 실행 패널과 결과 DataGrid를 구현한다.**

- [ ] **Step 4: 실행 결과에는 단일 진입 설정을 `IsReadOnly=True` TextBox로만 표시한다.**

- [ ] **Step 5: 테스트 프로젝트 전체를 실행한다.**

Run: `rtk dotnet test tests\AssetPilot.App.Tests`

### Task 3: 전체 검증

**Files:**
- Verify: `src/AssetPilot.App/BacktestWindow.xaml`
- Verify: `src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs`

- [ ] **Step 1: 프로젝트를 빌드한다.**

Run: `rtk dotnet build src\AssetPilot.App\AssetPilot.App.csproj`

- [ ] **Step 2: 앱 테스트 전체를 실행한다.**

Run: `rtk dotnet test tests\AssetPilot.App.Tests`
