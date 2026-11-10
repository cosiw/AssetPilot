using AssetPilot.Core.Accounts;
using AssetPilot.Core.Brokerage;
using AssetPilot.Core.Snapshots;
using AssetPilot.Infrastructure.Accounts;
using AssetPilot.Infrastructure.Kiwoom;
using AssetPilot.Infrastructure.Snapshots;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var databaseDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AssetPilot");
Directory.CreateDirectory(databaseDirectory);
var logDirectory = Path.Combine(databaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
var databaseConnectionString = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(databaseDirectory, "assetpilot.db")
}.ToString();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(logDirectory, "snapshot-worker-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Snapshot worker started.");
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IWindowsCredentialManager, WindowsCredentialManager>();
    builder.Services.AddSingleton<IKiwoomAccountStore, WindowsKiwoomAccountStore>();
    builder.Services.AddSingleton(new HttpClient());
    builder.Services.AddSingleton<KiwoomClient>();
    builder.Services.AddSingleton<IKiwoomPortfolioClient>(services => services.GetRequiredService<KiwoomClient>());
    builder.Services.AddSingleton<IKiwoomCashBalanceClient>(services => services.GetRequiredService<KiwoomClient>());
    builder.Services.AddSingleton<IKiwoomTransactionClient>(services => services.GetRequiredService<KiwoomClient>());
    builder.Services.AddSingleton<IAccountSnapshotStore>(
        new SqliteAccountSnapshotStore(databaseConnectionString));
    builder.Services.AddTransient<AccountSnapshotRunner>();

    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var snapshotStore = (SqliteAccountSnapshotStore)scope.ServiceProvider.GetRequiredService<IAccountSnapshotStore>();
    await snapshotStore.InitializeAsync();

    var result = await scope.ServiceProvider.GetRequiredService<AccountSnapshotRunner>().RunAsync();
    Log.Information(
        "Snapshot worker completed. Accounts={AccountCount}, Snapshots={SnapshotCount}, Transactions={TransactionCount}, Failures={FailureCount}",
        result.ProcessedAccountCount,
        result.SavedSnapshotCount,
        result.UpsertedTransactionCount,
        result.FailedAccountCount);

    if (result.ProcessedAccountCount > 0)
    {
        Console.WriteLine(
            $"accounts={result.ProcessedAccountCount}, snapshots={result.SavedSnapshotCount}, " +
            $"transactions={result.UpsertedTransactionCount}, failures={result.FailedAccountCount}");
    }

    return result.Succeeded ? 0 : 1;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Snapshot worker terminated unexpectedly.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
