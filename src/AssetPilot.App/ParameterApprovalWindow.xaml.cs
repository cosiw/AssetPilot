using System.Windows;
using AssetPilot.App.ViewModels;

namespace AssetPilot.App;

public partial class ParameterApprovalWindow : Window
{
    public ParameterApprovalWindow(ParameterApprovalViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadCommand.ExecuteAsync(null);
    }
}
