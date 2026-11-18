using AssetPilot.Backtest.Strategy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetPilot.App.ViewModels;

public partial class ParameterApprovalViewModel(ParameterDecisionStore store) : ObservableObject
{
    [ObservableProperty] public partial ParameterRecommendation? Recommendation { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        Recommendation = await store.LoadLatestPendingAsync();
        StatusMessage = Recommendation is null ? "승인 대기 중인 추천이 없습니다." : "비용·세금·슬리피지는 백테스트에 반영되지 않았습니다.";
    }

    [RelayCommand(CanExecute = nameof(CanApprove))]
    private async Task ApproveAsync()
    {
        if (Recommendation is null) return;
        await store.ApproveAsync(Recommendation, Environment.UserName);
        StatusMessage = "추천 설정을 활성화했습니다.";
        Recommendation = null;
    }

    private bool CanApprove() => Recommendation is not null;

    [RelayCommand(CanExecute = nameof(CanApprove))]
    private async Task RejectAsync()
    {
        if (Recommendation is null) return;
        await store.RejectAsync(Recommendation, Environment.UserName);
        StatusMessage = "추천 설정을 거절했습니다. 기존 활성 설정을 유지합니다.";
        Recommendation = null;
    }

    partial void OnRecommendationChanged(ParameterRecommendation? value)
    {
        ApproveCommand.NotifyCanExecuteChanged();
        RejectCommand.NotifyCanExecuteChanged();
    }
}
