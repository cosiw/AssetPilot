using System.Windows;
using AssetPilot.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AssetPilot.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        DataContext = viewModel;
    }

    private async void OpenAccountManagementButton_Click(object sender, RoutedEventArgs e)
    {
        var window = _serviceProvider.GetRequiredService<AccountManagementWindow>();
        window.Owner = this;
        window.ShowDialog();
        await _viewModel.InitializeCommand.ExecuteAsync(null);
    }
}
