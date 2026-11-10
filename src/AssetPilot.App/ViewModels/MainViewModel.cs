using System.Collections.ObjectModel;
using AssetPilot.Core.Accounts;
using AssetPilot.Core.Portfolio;
using AssetPilot.Core.Snapshots;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetPilot.App.ViewModels;

public partial class MainViewModel(
    IKiwoomAccountStore accountStore,
    IAccountSnapshotReader snapshotReader,
    AssetHistoryViewModel assetHistory) : ObservableObject
{
    private bool _suppressSelectedAccountLoad;

    public ObservableCollection<KiwoomAccountProfile> Accounts { get; } = [];

    public AssetHistoryViewModel AssetHistory { get; } = assetHistory;

    [ObservableProperty]
    public partial string Title { get; set; } = "AssetPilot";

    [ObservableProperty]
    public partial KiwoomAccountProfile? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial Portfolio? Portfolio { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SnapshotCapturedAtKorea))]
    public partial DateTimeOffset? SnapshotCapturedAtUtc { get; set; }

    public DateTimeOffset? SnapshotCapturedAtKorea => SnapshotCapturedAtUtc?.ToOffset(TimeSpan.FromHours(9));

    [ObservableProperty]
    public partial bool HasCashBalance { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsBusy = true;

        try
        {
            var accounts = await accountStore.GetAllAsync();

            Accounts.Clear();
            foreach (var account in accounts.OrderBy(account => account.AccountNumber, StringComparer.Ordinal))
            {
                Accounts.Add(account);
            }

            _suppressSelectedAccountLoad = true;
            SelectedAccount = Accounts.FirstOrDefault();
            _suppressSelectedAccountLoad = false;

            if (SelectedAccount is null)
            {
                Portfolio = null;
                SnapshotCapturedAtUtc = null;
                HasCashBalance = false;
                AssetHistory.Clear();
                StatusMessage = "등록된 계좌가 없습니다. 계좌 관리에서 계좌를 추가하세요.";
                return;
            }

            await LoadSelectedAccountSnapshotAsync();
        }
        catch (Exception)
        {
            Accounts.Clear();
            SelectedAccount = null;
            Portfolio = null;
            SnapshotCapturedAtUtc = null;
            HasCashBalance = false;
            AssetHistory.Clear();
            StatusMessage = "계좌 목록을 불러오지 못했습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedAccountChanged(KiwoomAccountProfile? value)
    {
        if (!_suppressSelectedAccountLoad)
        {
            _ = LoadSelectedAccountSnapshotAsync();
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadSelectedAccountSnapshotAsync();

    private async Task LoadSelectedAccountSnapshotAsync()
    {
        if (SelectedAccount is null)
        {
            Portfolio = null;
            SnapshotCapturedAtUtc = null;
            HasCashBalance = false;
            AssetHistory.Clear();
            return;
        }

        IsBusy = true;

        try
        {
            var summaries = await snapshotReader.GetSummariesAsync(SelectedAccount.AccountId, null, null);
            var latestSummary = summaries.LastOrDefault();
            if (latestSummary is null)
            {
                Portfolio = null;
                SnapshotCapturedAtUtc = null;
                HasCashBalance = false;
                await AssetHistory.LoadAsync(SelectedAccount.AccountId);
                StatusMessage = "저장된 자산 기록이 없습니다. 다음 예약 저장 후 표시됩니다.";
                return;
            }

            var detail = await snapshotReader.GetSnapshotAsync(SelectedAccount.AccountId, latestSummary.SnapshotDate);
            if (detail is null)
            {
                throw new InvalidOperationException("Latest account snapshot could not be loaded.");
            }

            Portfolio = new Portfolio(
                detail.Summary.TotalPurchaseAmount,
                detail.Summary.TotalEvaluationAmount,
                detail.Summary.TotalProfitLoss,
                detail.Summary.TotalProfitRateHundredths / 100m,
                detail.Positions.Select(position => new PortfolioPosition(
                    position.StockCode,
                    position.StockName,
                    checked((int)position.Quantity),
                    position.AveragePurchasePrice,
                    position.CurrentPrice,
                    position.PurchaseAmount,
                    position.EvaluationAmount,
                    position.ProfitLoss,
                    position.ProfitRateHundredths / 100m)).ToArray(),
                detail.Summary.CashBalance,
                detail.Summary.TotalAssetAmount);
            SnapshotCapturedAtUtc = detail.Summary.CapturedAtUtc;
            HasCashBalance = detail.Summary.HasCashBalance;
            await AssetHistory.LoadAsync(SelectedAccount.AccountId);
            StatusMessage = string.Empty;
        }
        catch (Exception)
        {
            Portfolio = null;
            SnapshotCapturedAtUtc = null;
            HasCashBalance = false;
            AssetHistory.Clear();
            StatusMessage = "저장된 자산 기록을 불러오지 못했습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
