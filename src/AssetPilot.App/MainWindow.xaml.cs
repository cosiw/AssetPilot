using System.Windows;
using AssetPilot.App.ViewModels;

namespace AssetPilot.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
