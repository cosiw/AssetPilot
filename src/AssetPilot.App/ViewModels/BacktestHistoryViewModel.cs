using System.Collections.ObjectModel;
using AssetPilot.Backtest.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetPilot.App.ViewModels;

public partial class BacktestHistoryViewModel(IBacktestRunStore store) : ObservableObject
{
    public ObservableCollection<BacktestRunSummary> Runs { get; } = [];

    [ObservableProperty]
    public partial BacktestRunSummary? SelectedRun { get; set; }

    [ObservableProperty]
    public partial BacktestRun? SelectedDetail { get; set; }

    public ObservableCollection<BacktestRunSummary> ComparisonRuns { get; } = [];

    [RelayCommand]
    private void ToggleComparison(BacktestRunSummary? run)
    {
        if (run is null) return;
        if (ComparisonRuns.Contains(run))
        {
            ComparisonRuns.Remove(run);
            return;
        }
        if (ComparisonRuns.Count < 3) ComparisonRuns.Add(run);
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Runs.Clear();
        foreach (var run in await store.GetSummariesAsync()) Runs.Add(run);
    }

    partial void OnSelectedRunChanged(BacktestRunSummary? value) => _ = LoadDetailAsync(value);

    private async Task LoadDetailAsync(BacktestRunSummary? summary) => SelectedDetail = summary is null ? null : await store.GetAsync(summary.Id);
}
