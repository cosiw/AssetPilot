using System.Windows;
using AssetPilot.App.ViewModels;

namespace AssetPilot.App;

public partial class AccountManagementWindow : Window
{
    private readonly AccountManagementViewModel _viewModel;

    public AccountManagementWindow(AccountManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCommand.ExecuteAsync(SecretKeyPasswordBox.Password);
        SecretKeyPasswordBox.Password = string.Empty;
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DeleteCommand.ExecuteAsync(null);
    }
}
