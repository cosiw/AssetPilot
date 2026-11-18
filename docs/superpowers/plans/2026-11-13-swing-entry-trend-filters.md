# ATR Swing Entry Trend Filters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Require positive long-SMA slope, minimum price distance above long SMA, and short-SMA-over-long-SMA before the ATR swing strategy creates an entry plan.

**Architecture:** Extend StrategyOptions with three configurable filter values. SwingAtrStrategy evaluates filters in deterministic order and exposes rejection reasons through the existing diagnostic interface. The WPF input and result windows bind directly to the new option properties.

**Tech Stack:** .NET 10, C#, xUnit, CommunityToolkit.Mvvm, WPF/XAML.

---

### Task 1: Define filter options and strategy behavior

**Files:**
- Modify: src/AssetPilot.Backtest/Strategy/StrategyOptions.cs
- Modify: src/AssetPilot.Backtest/Strategy/SwingAtrStrategy.cs
- Test: tests/AssetPilot.Backtest.Tests/SwingAtrStrategyTests.cs

- [x] **Step 1: Write failing tests for every filter**

~~~csharp
[Fact]
public void EvaluateAtClose_rejects_a_non_positive_long_sma_slope()
{
    var strategy = new SwingAtrStrategy(new StrategyOptions
    {
        SmaPeriod = 3, AtrPeriod = 1, ShortSmaPeriod = 2,
        SmaSlopeLookbackDays = 2, MinimumSmaDistancePercent = 0m
    });
    var bars = CreateBars(11m, 11m, 10m, 10m, 11m);

    Assert.Null(strategy.EvaluateAtClose(bars));
    Assert.Equal("LongSmaSlopeNotPositive", strategy.ExplainNoEntry(bars)?.Code);
}

[Fact]
public void EvaluateAtClose_rejects_a_close_that_is_not_one_percent_above_long_sma()
{
    var strategy = new SwingAtrStrategy(new StrategyOptions
    {
        SmaPeriod = 3, AtrPeriod = 1, ShortSmaPeriod = 2,
        SmaSlopeLookbackDays = 1, MinimumSmaDistancePercent = 1m
    });
    var bars = CreateBars(10m, 10m, 10m, 10.05m);

    Assert.Null(strategy.EvaluateAtClose(bars));
    Assert.Equal("MinimumSmaDistanceNotMet", strategy.ExplainNoEntry(bars)?.Code);
}

[Fact]
public void EvaluateAtClose_rejects_when_short_sma_is_not_above_long_sma()
{
    var strategy = new SwingAtrStrategy(new StrategyOptions
    {
        SmaPeriod = 4, AtrPeriod = 1, ShortSmaPeriod = 2,
        SmaSlopeLookbackDays = 1, MinimumSmaDistancePercent = 0m
    });
    var bars = CreateBars(8m, 10m, 12m, 10m, 11m);

    Assert.Null(strategy.EvaluateAtClose(bars));
    Assert.Equal("ShortSmaNotAboveLongSma", strategy.ExplainNoEntry(bars)?.Code);
}
~~~

- [x] **Step 2: Run the tests and verify RED**

Run: rtk proxy dotnet test tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore --filter FullyQualifiedName~SwingAtrStrategyTests

Expected: FAIL because the new option members and filter behavior are absent.

- [x] **Step 3: Add the agreed defaults**

~~~csharp
public int SmaSlopeLookbackDays { get; init; } = 5;
public decimal MinimumSmaDistancePercent { get; init; } = 1m;
public int ShortSmaPeriod { get; init; } = 20;
~~~

- [x] **Step 4: Implement shared filter evaluation before ATR bounds**

Ensure the required history length is the maximum of SmaPeriod + SmaSlopeLookbackDays, ShortSmaPeriod, and AtrPeriod. In this order, reject: long-SMA slope that is zero or negative; close below longSma * (1 + minimumDistance / 100); short SMA that is less than or equal to long SMA. Use the same private evaluation result in both EvaluateAtClose and ExplainNoEntry.

- [x] **Step 5: Run the tests and verify GREEN**

Run: rtk proxy dotnet test tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore --filter FullyQualifiedName~SwingAtrStrategyTests

Expected: PASS.

### Task 2: Surface filter rejection diagnostics

**Files:**
- Modify: src/AssetPilot.Backtest/Execution/BacktestDiagnostic.cs
- Test: tests/AssetPilot.Backtest.Tests/BacktestDiagnosticTests.cs

- [x] **Step 1: Write failing diagnostic display-name tests**

~~~csharp
[Theory]
[InlineData("LongSmaSlopeNotPositive", "장기 이평 기울기 미충족")]
[InlineData("MinimumSmaDistanceNotMet", "최소 SMA 이격도 미충족")]
[InlineData("ShortSmaNotAboveLongSma", "단기 이평 추세 미충족")]
public void Diagnostic_exposes_a_Korean_display_name_for_each_trend_filter(string code, string expected)
{
    Assert.Equal(expected, new BacktestDiagnostic(code, new DateOnly(2026, 1, 2), string.Empty).DisplayName);
}
~~~

- [x] **Step 2: Run diagnostic tests and verify RED**

Run: rtk proxy dotnet test tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore --filter FullyQualifiedName~BacktestDiagnosticTests

Expected: FAIL because DisplayName falls through to the raw code.

- [x] **Step 3: Map the three diagnostic codes**

~~~csharp
"LongSmaSlopeNotPositive" => "장기 이평 기울기 미충족",
"MinimumSmaDistanceNotMet" => "최소 SMA 이격도 미충족",
"ShortSmaNotAboveLongSma" => "단기 이평 추세 미충족",
~~~

- [x] **Step 4: Run diagnostic tests and verify GREEN**

Run: rtk proxy dotnet test tests\AssetPilot.Backtest.Tests\AssetPilot.Backtest.Tests.csproj --no-restore --filter FullyQualifiedName~BacktestDiagnosticTests

Expected: PASS.

### Task 3: Configure and validate filters in the backtest view model

**Files:**
- Modify: src/AssetPilot.App/ViewModels/BacktestRunViewModel.cs
- Test: tests/AssetPilot.App.Tests/BacktestRunViewModelTests.cs

- [x] **Step 1: Write a failing validation test**

~~~csharp
[Theory]
[InlineData(0, 1m, 20)]
[InlineData(5, -0.01, 20)]
[InlineData(5, 1m, 0)]
public async Task Run_rejects_invalid_trend_filter_values(int slopeDays, decimal minimumDistance, int shortSmaDays)
{
    var model = CreateValidModel();
    model.SmaSlopeLookbackDays = slopeDays;
    model.MinimumSmaDistancePercent = minimumDistance;
    model.ShortSmaPeriod = shortSmaDays;

    await model.RunCommand.ExecuteAsync(null);

    Assert.NotEmpty(model.StatusMessage);
}
~~~

- [x] **Step 2: Run view-model tests and verify RED**

Run: rtk proxy dotnet test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore --filter FullyQualifiedName~BacktestRunViewModelTests

Expected: FAIL to compile because the bindable properties are absent.

- [x] **Step 3: Add properties, validation, and option mapping**

~~~csharp
[ObservableProperty] public partial int SmaSlopeLookbackDays { get; set; } = 5;
[ObservableProperty] public partial decimal MinimumSmaDistancePercent { get; set; } = 1m;
[ObservableProperty] public partial int ShortSmaPeriod { get; set; } = 20;
~~~

Reject non-positive slope/short-SMA periods and negative distance before CSV loading. Copy the three values to ExecutedOptions.

- [x] **Step 4: Run view-model tests and verify GREEN**

Run: rtk proxy dotnet test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore --filter FullyQualifiedName~BacktestRunViewModelTests

Expected: PASS.

### Task 4: Add filter inputs and execution-setting display

**Files:**
- Modify: src/AssetPilot.App/BacktestInputWindow.xaml
- Modify: src/AssetPilot.App/BacktestWindow.xaml
- Test: tests/AssetPilot.App.Tests/BacktestWindowLayoutTests.cs

- [x] **Step 1: Write failing layout assertions**

~~~csharp
Assert.Contains("SmaSlopeLookbackDays", inputXaml);
Assert.Contains("MinimumSmaDistancePercent", inputXaml);
Assert.Contains("ShortSmaPeriod", inputXaml);
Assert.Contains("ExecutedOptions.SmaSlopeLookbackDays", resultXaml);
Assert.Contains("ExecutedOptions.MinimumSmaDistancePercent", resultXaml);
Assert.Contains("ExecutedOptions.ShortSmaPeriod", resultXaml);
~~~

- [x] **Step 2: Run layout tests and verify RED**

Run: rtk proxy dotnet test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore --filter FullyQualifiedName~BacktestWindowLayoutTests

Expected: FAIL because the controls and result bindings are absent.

- [x] **Step 3: Add three input controls**

Use TextBox.Backtest and these bindings:

~~~xml
<TextBox Style="{StaticResource TextBox.Backtest}" Text="{Binding SmaSlopeLookbackDays, UpdateSourceTrigger=PropertyChanged}" />
<TextBox Style="{StaticResource TextBox.Backtest}" Text="{Binding MinimumSmaDistancePercent, UpdateSourceTrigger=LostFocus}" />
<TextBox Style="{StaticResource TextBox.Backtest}" Text="{Binding ShortSmaPeriod, UpdateSourceTrigger=PropertyChanged}" />
~~~

Label them 기울기 비교 기간 (거래일), 최소 SMA 이격도 (%), and 단기 이동평균 기간. Add helper text stating that all filters must pass before an ATR buy band is created.

- [x] **Step 4: Show the executed values on the result screen**

~~~xml
<TextBlock Text="{Binding ExecutedOptions.SmaSlopeLookbackDays, StringFormat={}{0}거래일}" />
<TextBlock Text="{Binding ExecutedOptions.MinimumSmaDistancePercent, StringFormat={}{0:N2}%}" />
<TextBlock Text="{Binding ExecutedOptions.ShortSmaPeriod, StringFormat={}{0}일}" />
~~~

- [x] **Step 5: Run layout tests and verify GREEN**

Run: rtk proxy dotnet test tests\AssetPilot.App.Tests\AssetPilot.App.Tests.csproj --no-restore --filter FullyQualifiedName~BacktestWindowLayoutTests

Expected: PASS.

### Task 5: Full regression verification

**Files:**
- Verify only: AssetPilot.slnx

- [ ] **Step 1: Run the full solution test suite**

Run: rtk proxy dotnet test AssetPilot.slnx --no-restore

Expected: PASS. Existing OpenTK and SkiaSharp NU1701 compatibility warnings may remain, but no test may fail.

- [ ] **Step 2: Inspect whitespace and changed files**

Run: rtk git diff --check

Expected: no whitespace errors.

