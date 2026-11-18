using AssetPilot.Backtest.MarketData;
using AssetPilot.Core.Accounts;
using AssetPilot.Infrastructure.Accounts;
using AssetPilot.Infrastructure.Kiwoom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AssetPilot.MarketDataWorker;

internal static class Program
{
    public static async Task<int> Main()
    {
        var builder = Host.CreateApplicationBuilder();
        var root = Environment.GetEnvironmentVariable("AssetPilot__MarketDataRoot") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssetPilot", "data");
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IWindowsCredentialManager, WindowsCredentialManager>();
        builder.Services.AddSingleton<IKiwoomAccountStore, WindowsKiwoomAccountStore>();
        builder.Services.AddSingleton(new HttpClient());
        builder.Services.AddSingleton<IMarketDataProvider, KiwoomMarketDataProvider>();
        builder.Services.AddSingleton(new CsvMarketDataStore(root));
        builder.Services.AddSingleton<MarketDataRunner>();
        using var host = builder.Build();
        try { await host.Services.GetRequiredService<MarketDataRunner>().RunAsync(); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }
}
