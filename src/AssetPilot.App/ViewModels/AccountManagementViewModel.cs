using System.Collections.ObjectModel;
using AssetPilot.Core.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetPilot.App.ViewModels;

public partial class AccountManagementViewModel(IKiwoomAccountStore accountStore) : ObservableObject
{
    public ObservableCollection<KiwoomAccountProfile> Accounts { get; } = [];

    [ObservableProperty]
    public partial KiwoomAccountProfile? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial string AccountNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial KiwoomEnvironment Environment { get; set; } = KiwoomEnvironment.Real;

    [ObservableProperty]
    public partial string AppKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    partial void OnSelectedAccountChanged(KiwoomAccountProfile? value)
    {
        if (value is null)
        {
            return;
        }

        AccountNumber = value.AccountNumber;
        Environment = value.Environment;
        AppKey = string.Empty;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var accounts = await accountStore.GetAllAsync();
            Accounts.Clear();

            foreach (var account in accounts.OrderBy(account => account.AccountNumber, StringComparer.Ordinal))
            {
                Accounts.Add(account);
            }
        }
        catch (Exception)
        {
            StatusMessage = "계좌 목록을 불러오지 못했습니다.";
        }
    }

    [RelayCommand]
    private async Task SaveAsync(string? secretKey)
    {
        if (string.IsNullOrWhiteSpace(AccountNumber)
            || string.IsNullOrWhiteSpace(AppKey)
            || string.IsNullOrWhiteSpace(secretKey))
        {
            StatusMessage = "계좌번호, App Key, Secret Key를 모두 입력하세요.";
            return;
        }

        try
        {
            var profile = new KiwoomAccountProfile(AccountNumber.Trim(), Environment);
            var credentials = new KiwoomAccountCredentials(profile, AppKey.Trim(), secretKey);

            await accountStore.SaveAsync(credentials);
            ResetInput();
            await LoadAsync();
            StatusMessage = "계좌를 저장했습니다.";
        }
        catch (Exception)
        {
            StatusMessage = "계좌를 저장하지 못했습니다.";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedAccount is null)
        {
            StatusMessage = "삭제할 계좌를 선택하세요.";
            return;
        }

        try
        {
            await accountStore.DeleteAsync(SelectedAccount.AccountNumber);
            ResetInput();
            await LoadAsync();
            StatusMessage = "계좌를 삭제했습니다.";
        }
        catch (Exception)
        {
            StatusMessage = "계좌를 삭제하지 못했습니다.";
        }
    }

    private void ResetInput()
    {
        SelectedAccount = null;
        AccountNumber = string.Empty;
        Environment = KiwoomEnvironment.Real;
        AppKey = string.Empty;
    }
}
