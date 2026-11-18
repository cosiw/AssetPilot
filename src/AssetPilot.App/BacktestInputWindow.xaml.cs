using Microsoft.Win32;
using System.Windows;
using AssetPilot.App.ViewModels;

namespace AssetPilot.App;

public partial class BacktestInputWindow : Window
{
    private readonly BacktestRunViewModel _viewModel;
    private readonly BacktestHistoryViewModel _history;
    private readonly BacktestExperimentViewModel _experimentViewModel;

    public BacktestInputWindow(BacktestRunViewModel viewModel, BacktestHistoryViewModel history, BacktestExperimentViewModel experimentViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _history = history;
        _experimentViewModel = experimentViewModel;
        DataContext = viewModel;
        viewModel.RunCompleted += OpenDashboard;
    }

    private void OpenDashboard(object? sender, EventArgs e)
    {
        var dashboard = new BacktestWindow(_viewModel, _history) { Owner = this };
        dashboard.Show();
    }

    private void SelectDailyCsvButton_Click(object sender, RoutedEventArgs e) =>
        SelectCsvFile(path => _viewModel.DailyCsvPath = path);

    private void SelectFiveMinuteCsvButton_Click(object sender, RoutedEventArgs e) =>
        SelectCsvFile(path => _viewModel.FiveMinuteCsvPath = path);

    private void OpenExperimentWindowButton_Click(object sender, RoutedEventArgs e) =>
        new BacktestExperimentWindow(_experimentViewModel) { Owner = this }.Show();

    private void SelectCsvFile(Action<string> setPath)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true) setPath(dialog.FileName);
    }
}
