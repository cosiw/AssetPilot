using System.IO;
using System.Windows;
using AssetPilot.App.ViewModels;
using AssetPilot.Core.Accounts;
using AssetPilot.Core.Snapshots;
using AssetPilot.Infrastructure.Accounts;
using AssetPilot.Infrastructure.Snapshots;
using AssetPilot.Infrastructure.Backtests;
using AssetPilot.Backtest.Storage;
using AssetPilot.Backtest.Strategy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssetPilot.App;

/// <summary>
/// Composition Root: 포트(인터페이스)에 어떤 어댑터(구현체)를 꽂을지 여기서 조립한다.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IWindowsCredentialManager, WindowsCredentialManager>();
                services.AddSingleton<IKiwoomAccountStore, WindowsKiwoomAccountStore>();
                services.AddSingleton(TimeProvider.System);

                var databaseDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AssetPilot");
                Directory.CreateDirectory(databaseDirectory);
                var snapshotStore = new SqliteAccountSnapshotStore(
                    $"Data Source={Path.Combine(databaseDirectory, "assetpilot.db")}");
                services.AddSingleton(snapshotStore);
                services.AddSingleton<IAccountSnapshotStore>(snapshotStore);
                services.AddSingleton<IAccountSnapshotReader>(snapshotStore);
                var backtestStore = new SqliteBacktestRunStore($"Data Source={Path.Combine(databaseDirectory, "assetpilot.db")}");
                services.AddSingleton(backtestStore);
                services.AddSingleton<IBacktestRunStore>(backtestStore);
                services.AddSingleton<IBacktestRunExporter, BacktestRunCsvExporter>();
                services.AddSingleton<BacktestExperimentCsvExporter>();
                var marketDataRoot = Environment.GetEnvironmentVariable("AssetPilot__MarketDataRoot") ?? Path.Combine(databaseDirectory, "data");
                services.AddSingleton(new ParameterDecisionStore(marketDataRoot));

                services.AddSingleton<AssetHistoryViewModel>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<BacktestRunViewModel>();
                services.AddTransient<BacktestExperimentViewModel>();
                services.AddTransient<BacktestHistoryViewModel>();
                services.AddTransient<ParameterApprovalViewModel>();
                services.AddTransient<ParameterApprovalWindow>();
                services.AddTransient<BacktestWindow>();
                services.AddTransient<BacktestExperimentWindow>();
                services.AddTransient<BacktestInputWindow>();
                services.AddTransient<AccountManagementViewModel>();
                services.AddTransient<AccountManagementWindow>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        await _host.Services.GetRequiredService<SqliteAccountSnapshotStore>().InitializeAsync();
        await _host.Services.GetRequiredService<SqliteBacktestRunStore>().InitializeAsync();
        var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
        await mainViewModel.InitializeCommand.ExecuteAsync(null);

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
