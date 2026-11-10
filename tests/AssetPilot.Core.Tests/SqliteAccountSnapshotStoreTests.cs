using AssetPilot.Core.Snapshots;
using AssetPilot.Core.Transactions;
using AssetPilot.Infrastructure.Snapshots;
using Microsoft.Data.Sqlite;

namespace AssetPilot.Core.Tests;

public sealed class SqliteAccountSnapshotStoreTests
{
    [Fact]
    public async Task SaveSnapshot_and_upsert_transactions_are_idempotent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new SqliteAccountSnapshotStore(database.ConnectionString);
        var snapshot = CreateSnapshot();
        var transaction = CreateDividend(snapshot.AccountId, "same-key");

        await store.InitializeAsync();
        await store.SaveSnapshotAsync(snapshot);
        await store.SaveSnapshotAsync(snapshot);
        await store.UpsertTransactionsAsync([transaction]);
        await store.UpsertTransactionsAsync([transaction]);

        Assert.Equal(1, await database.CountAsync("account_snapshots"));
        Assert.Equal(1, await database.CountAsync("position_snapshots"));
        Assert.Equal(1, await database.CountAsync("account_transactions"));
    }

    [Fact]
    public async Task SaveSnapshot_rolls_back_header_when_a_position_insert_fails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new SqliteAccountSnapshotStore(database.ConnectionString);
        var snapshot = CreateSnapshot() with
        {
            Positions =
            [
                new PositionSnapshot("KRX", "005930", "삼성전자", 10, 70_000m, 75_000m, 700_000m, 750_000m, 50_000m, 714),
                new PositionSnapshot("KRX", "005930", "삼성전자", 20, 70_000m, 75_000m, 1_400_000m, 1_500_000m, 100_000m, 714)
            ]
        };

        await store.InitializeAsync();

        await Assert.ThrowsAsync<SqliteException>(() => store.SaveSnapshotAsync(snapshot));

        Assert.Equal(0, await database.CountAsync("account_snapshots"));
        Assert.Equal(0, await database.CountAsync("position_snapshots"));
    }

    [Fact]
    public async Task GetLatestTransactionDateAsync_returns_the_latest_date_for_the_account()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new SqliteAccountSnapshotStore(database.ConnectionString);
        var accountId = Guid.NewGuid();

        await store.InitializeAsync();
        await store.UpsertTransactionsAsync(
        [
            CreateDividend(accountId, "older", new DateOnly(2026, 11, 7)),
            CreateDividend(accountId, "newer", new DateOnly(2026, 11, 9)),
            CreateDividend(Guid.NewGuid(), "other-account", new DateOnly(2026, 11, 10))
        ]);

        Assert.Equal(new DateOnly(2026, 11, 9), await store.GetLatestTransactionDateAsync(accountId));
    }

    [Fact]
    public async Task InitializeAsync_migrates_existing_snapshots_with_cash_balance_marked_as_not_captured()
    {
        await using var database = await TestDatabase.CreateAsync();
        var accountId = Guid.NewGuid();
        await database.ExecuteAsync($"""
            CREATE TABLE account_snapshots (
                account_id TEXT NOT NULL,
                snapshot_date TEXT NOT NULL,
                captured_at_utc TEXT NOT NULL,
                cash_balance TEXT NOT NULL,
                total_asset_amount TEXT NOT NULL,
                total_purchase_amount TEXT NOT NULL,
                total_evaluation_amount TEXT NOT NULL,
                total_profit_loss TEXT NOT NULL,
                total_profit_rate_hundredths INTEGER NOT NULL,
                source TEXT NOT NULL,
                PRIMARY KEY (account_id, snapshot_date)
            );

            INSERT INTO account_snapshots VALUES (
                '{accountId:D}', '2026-11-08', '2026-11-08T08:00:00.0000000Z', '0',
                '1750000', '1400000', '1750000', '350000', 2500, 'Kiwoom'
            );
            """);
        var store = new SqliteAccountSnapshotStore(database.ConnectionString);

        await store.InitializeAsync();
        await store.InitializeAsync();

        Assert.True(await database.ColumnExistsAsync("account_snapshots", "cash_balance_captured"));
        var summary = Assert.Single(await store.GetSummariesAsync(accountId, null, null));
        Assert.False(summary.HasCashBalance);
        Assert.Equal(0m, summary.CashBalance);
    }

    [Fact]
    public async Task GetSummariesAsync_returns_cash_capture_status_in_date_order_and_honors_bounds()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new SqliteAccountSnapshotStore(database.ConnectionString);
        var accountId = Guid.NewGuid();
        var first = CreateSnapshot() with
        {
            AccountId = accountId,
            SnapshotDate = new DateOnly(2026, 11, 7),
            HasCashBalance = false
        };
        var second = first with
        {
            SnapshotDate = new DateOnly(2026, 11, 9),
            CapturedAtUtc = DateTimeOffset.Parse("2026-11-09T08:00:00+00:00"),
            CashBalance = 1_200_000m,
            HasCashBalance = true
        };

        await store.InitializeAsync();
        await store.SaveSnapshotAsync(second);
        await store.SaveSnapshotAsync(first);

        var summaries = await store.GetSummariesAsync(accountId, null, null);
        Assert.Collection(
            summaries,
            summary =>
            {
                Assert.Equal(new DateOnly(2026, 11, 7), summary.SnapshotDate);
                Assert.False(summary.HasCashBalance);
            },
            summary =>
            {
                Assert.Equal(new DateOnly(2026, 11, 9), summary.SnapshotDate);
                Assert.True(summary.HasCashBalance);
                Assert.Equal(1_200_000m, summary.CashBalance);
            });

        var bounded = await store.GetSummariesAsync(
            accountId,
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 9));
        Assert.Equal(new DateOnly(2026, 11, 9), Assert.Single(bounded).SnapshotDate);
    }

    [Fact]
    public async Task GetSnapshotAsync_returns_the_saved_header_and_positions()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new SqliteAccountSnapshotStore(database.ConnectionString);
        var snapshot = CreateSnapshot();

        await store.InitializeAsync();
        await store.SaveSnapshotAsync(snapshot);

        var detail = await store.GetSnapshotAsync(snapshot.AccountId, snapshot.SnapshotDate);

        Assert.NotNull(detail);
        Assert.Equal(snapshot.CashBalance, detail.Summary.CashBalance);
        var position = Assert.Single(detail.Positions);
        Assert.Equal("005930", position.StockCode);
        Assert.Equal(10, position.Quantity);
    }

    private static AccountSnapshot CreateSnapshot() =>
        new(
            Guid.NewGuid(),
            new DateOnly(2026, 11, 9),
            DateTimeOffset.Parse("2026-11-09T08:00:00+00:00"),
            1_000_000m,
            true,
            1_750_000m,
            1_400_000m,
            1_750_000m,
            350_000m,
            2_500,
            [new PositionSnapshot("KRX", "005930", "삼성전자", 10, 140_000m, 175_000m, 1_400_000m, 1_750_000m, 350_000m, 2_500)],
            "Kiwoom");

    private static AccountTransaction CreateDividend(Guid accountId, string key, DateOnly? date = null) =>
        new(
            accountId,
            key,
            date ?? new DateOnly(2026, 11, 9),
            DateTimeOffset.Parse("2026-11-09T08:00:00+00:00"),
            TransactionType.Dividend,
            "배당",
            "005930",
            "삼성전자",
            null,
            null,
            10_000m,
            0m,
            0m,
            DateTimeOffset.Parse("2026-11-09T08:00:00+00:00"),
            "Kiwoom");

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string path;

        private TestDatabase(string path)
        {
            this.path = path;
            ConnectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        }

        public string ConnectionString { get; }

        public static Task<TestDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"AssetPilot-{Guid.NewGuid():N}.db");
            return Task.FromResult(new TestDatabase(path));
        }

        public async Task<int> CountAsync(string table)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<bool> ColumnExistsAsync(string table, string column)
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table});";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }
}
