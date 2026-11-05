using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetPilot.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = "AssetPilot";
}
