using System.Windows;
using AssetPilot.App.ViewModels;

namespace AssetPilot.App;

public partial class BacktestWindow : Window
{
    public BacktestWindow(BacktestRunViewModel viewModel, BacktestHistoryViewModel history)
    {
        InitializeComponent();
        DataContext = viewModel;
        History = history;
        viewModel.RunCompleted += async (_, _) => await History.LoadCommand.ExecuteAsync(null);
        Loaded += async (_, _) => await History.LoadCommand.ExecuteAsync(null);
    }

    public BacktestHistoryViewModel History { get; }
}
