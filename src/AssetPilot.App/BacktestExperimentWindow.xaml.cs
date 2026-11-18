using Microsoft.Win32;
using System.Windows;
using AssetPilot.App.ViewModels;

namespace AssetPilot.App;

public partial class BacktestExperimentWindow : Window
{
    public BacktestExperimentWindow(BacktestExperimentViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void SelectDailyCsvButton_Click(object sender, RoutedEventArgs e) =>
        SelectCsvFile(path => ViewModel.DailyCsvPath = path);

    private void SelectFiveMinuteCsvButton_Click(object sender, RoutedEventArgs e) =>
        SelectCsvFile(path => ViewModel.FiveMinuteCsvPath = path);

    private BacktestExperimentViewModel ViewModel => (BacktestExperimentViewModel)DataContext;

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
