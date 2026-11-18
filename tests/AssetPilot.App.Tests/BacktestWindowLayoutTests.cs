using System.IO;

namespace AssetPilot.App.Tests;

public sealed class BacktestWindowLayoutTests
{
    [Fact]
    public void Backtest_dashboard_shows_results_without_editable_strategy_inputs()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "AssetPilot.App", "BacktestWindow.xaml"));

        Assert.Contains("Result.Executions", xaml);
        Assert.Contains("백테스트 결과", xaml);
        Assert.Contains("실행 당시 설정", xaml);
        Assert.Contains("실행 완료", xaml);
        Assert.Contains("선택 거래의 체결 내역", xaml);
        Assert.Contains("StartingCash", xaml);
        Assert.Contains("AllocationPercent", xaml);
        Assert.Contains("SelectedExecution", xaml);
        Assert.Contains("Result.Diagnostics", xaml);
        Assert.Contains("ExecutedOptions.SmaSlopeLookbackDays", xaml);
        Assert.Contains("ExecutedOptions.MinimumSmaDistancePercent", xaml);
        Assert.Contains("ExecutedOptions.ShortSmaPeriod", xaml);
        Assert.DoesNotContain("UpdateSourceTrigger=PropertyChanged", xaml);
    }

    [Fact]
    public void Backtest_input_window_exposes_only_exit_rule_inputs()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "AssetPilot.App", "BacktestInputWindow.xaml"));

        Assert.Contains("RunCommand", xaml);
        Assert.Contains("TargetAtrMultiplier", xaml);
        Assert.Contains("StopAtrMultiplier", xaml);
        Assert.Contains("MaxHoldingDays", xaml);
        Assert.DoesNotContain("Binding SmaPeriod", xaml);
        Assert.DoesNotContain("Binding AtrPeriod", xaml);
        Assert.DoesNotContain("Binding BuyBandAtrMultiplier", xaml);
        Assert.DoesNotContain("Binding SmaSlopeLookbackDays", xaml);
        Assert.DoesNotContain("Binding MinimumSmaDistancePercent", xaml);
        Assert.DoesNotContain("Binding ShortSmaPeriod", xaml);
        Assert.Contains("입력 순서", xaml);
        Assert.Contains("일봉은 하루에 한 줄", xaml);
        Assert.Contains("TextBox.Backtest", xaml);
    }

    [Fact]
    public void Backtest_input_window_allows_decimal_exit_values_before_focus_changes()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "AssetPilot.App", "BacktestInputWindow.xaml"));

        Assert.Contains("TargetAtrMultiplier, UpdateSourceTrigger=LostFocus", xaml);
        Assert.Contains("StopAtrMultiplier, UpdateSourceTrigger=LostFocus", xaml);
    }

    [Fact]
    public void Backtest_input_window_provides_csv_file_picker_buttons()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "AssetPilot.App", "BacktestInputWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "AssetPilot.App", "BacktestInputWindow.xaml.cs"));

        Assert.Contains("SelectDailyCsvButton_Click", xaml);
        Assert.Contains("SelectFiveMinuteCsvButton_Click", xaml);
        Assert.Contains("OpenFileDialog", codeBehind);
        Assert.Contains("CSV files (*.csv)|*.csv", codeBehind);
    }

    [Fact]
    public void Backtest_input_window_opens_the_manual_exit_parameter_experiment_window()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AssetPilot.App", "BacktestInputWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "AssetPilot.App", "BacktestInputWindow.xaml.cs"));

        Assert.Contains("OpenExperimentWindowButton_Click", xaml);
        Assert.Contains("BacktestExperimentWindow", codeBehind);
        Assert.True(File.Exists(Path.Combine(root, "src", "AssetPilot.App", "BacktestExperimentWindow.xaml")));
    }

    [Fact]
    public void Backtest_experiment_window_provides_daily_and_five_minute_csv_file_pickers()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AssetPilot.App", "BacktestExperimentWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "AssetPilot.App", "BacktestExperimentWindow.xaml.cs"));

        Assert.Contains("SelectDailyCsvButton_Click", xaml);
        Assert.Contains("SelectFiveMinuteCsvButton_Click", xaml);
        Assert.Contains("OpenFileDialog", codeBehind);
        Assert.Contains("CSV files (*.csv)|*.csv", codeBehind);
    }

    [Fact]
    public void Backtest_experiment_window_shows_total_return_as_a_percentage()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AssetPilot.App", "BacktestExperimentWindow.xaml"));

        Assert.Contains("PerformanceReport.TotalReturn, StringFormat={}{0:P2}", xaml);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AssetPilot.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("AssetPilot repository root was not found.");
    }
}
