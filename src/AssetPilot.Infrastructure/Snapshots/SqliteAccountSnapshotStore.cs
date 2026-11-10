using System.Globalization;
using System.Text;
using AssetPilot.Core.Snapshots;
using AssetPilot.Core.Transactions;
using Microsoft.Data.Sqlite;

namespace AssetPilot.Infrastructure.Snapshots;

public sealed class SqliteAccountSnapshotStore(string connectionString) : IAccountSnapshotStore, IAccountSnapshotReader
{
    private const string DateFormat = "yyyy-MM-dd";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS account_snapshots (
                account_id TEXT NOT NULL,
                snapshot_date TEXT NOT NULL,
                captured_at_utc TEXT NOT NULL,
                cash_balance TEXT NOT NULL,
                cash_balance_captured INTEGER NOT NULL,
                total_asset_amount TEXT NOT NULL,
                total_purchase_amount TEXT NOT NULL,
                total_evaluation_amount TEXT NOT NULL,
                total_profit_loss TEXT NOT NULL,
                total_profit_rate_hundredths INTEGER NOT NULL,
                source TEXT NOT NULL,
                PRIMARY KEY (account_id, snapshot_date)
            );

            CREATE TABLE IF NOT EXISTS position_snapshots (
                account_id TEXT NOT NULL,
                snapshot_date TEXT NOT NULL,
                market TEXT NOT NULL,
                stock_code TEXT NOT NULL,
                stock_name TEXT NOT NULL,
                quantity INTEGER NOT NULL,
                average_purchase_price TEXT NOT NULL,
                current_price TEXT NOT NULL,
                purchase_amount TEXT NOT NULL,
                evaluation_amount TEXT NOT NULL,
                profit_loss TEXT NOT NULL,
                profit_rate_hundredths INTEGER NOT NULL,
                PRIMARY KEY (account_id, snapshot_date, stock_code),
                FOREIGN KEY (account_id, snapshot_date)
                    REFERENCES account_snapshots (account_id, snapshot_date)
            );

            CREATE TABLE IF NOT EXISTS account_transactions (
                account_id TEXT NOT NULL,
                source_transaction_key TEXT NOT NULL,
                transaction_date TEXT NOT NULL,
                occurred_at_utc TEXT NULL,
                transaction_type INTEGER NOT NULL,
                broker_transaction_type TEXT NOT NULL,
                stock_code TEXT NULL,
                stock_name TEXT NULL,
                quantity INTEGER NULL,
                unit_price TEXT NULL,
                cash_amount TEXT NOT NULL,
                fee_amount TEXT NOT NULL,
                tax_amount TEXT NOT NULL,
                captured_at_utc TEXT NOT NULL,
                source TEXT NOT NULL,
                UNIQUE (account_id, source_transaction_key)
            );

            CREATE INDEX IF NOT EXISTS ix_account_transactions_account_date
                ON account_transactions (account_id, transaction_date);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureCashBalanceCapturedColumnAsync(connection, cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid accountId, DateOnly snapshotDate, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM account_snapshots
                WHERE account_id = $accountId AND snapshot_date = $snapshotDate
            );
            """;
        command.Parameters.AddWithValue("$accountId", accountId.ToString("D"));
        command.Parameters.AddWithValue("$snapshotDate", FormatDate(snapshotDate));

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task SaveSnapshotAsync(AccountSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var inserted = await InsertSnapshotAsync(connection, transaction, snapshot, cancellationToken);
        if (inserted)
        {
            foreach (var position in snapshot.Positions)
            {
                await InsertPositionAsync(connection, transaction, snapshot, position, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DateOnly?> GetLatestTransactionDateAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MAX(transaction_date)
            FROM account_transactions
            WHERE account_id = $accountId;
            """;
        command.Parameters.AddWithValue("$accountId", accountId.ToString("D"));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : DateOnly.ParseExact((string)result, DateFormat, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<AccountSnapshotSummary>> GetSummariesAsync(
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var query = new StringBuilder(
            """
            SELECT snapshot_date, captured_at_utc, cash_balance, cash_balance_captured,
                   total_asset_amount, total_purchase_amount, total_evaluation_amount,
                   total_profit_loss, total_profit_rate_hundredths
            FROM account_snapshots
            WHERE account_id = $accountId
            """);
        command.Parameters.AddWithValue("$accountId", accountId.ToString("D"));
        query.AppendLine();

        if (from is { } startDate)
        {
            query.AppendLine("AND snapshot_date >= $from");
            command.Parameters.AddWithValue("$from", FormatDate(startDate));
        }

        if (to is { } endDate)
        {
            query.AppendLine("AND snapshot_date <= $to");
            command.Parameters.AddWithValue("$to", FormatDate(endDate));
        }

        query.AppendLine();
        query.AppendLine("ORDER BY snapshot_date;");
        command.CommandText = query.ToString();

        var summaries = new List<AccountSnapshotSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            summaries.Add(new AccountSnapshotSummary(
                DateOnly.ParseExact(reader.GetString(0), DateFormat, CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ParseDecimal(reader.GetString(2)),
                reader.GetInt64(3) == 1,
                ParseDecimal(reader.GetString(4)),
                ParseDecimal(reader.GetString(5)),
                ParseDecimal(reader.GetString(6)),
                ParseDecimal(reader.GetString(7)),
                reader.GetInt32(8)));
        }

        return summaries;
    }

    public async Task<AccountSnapshotDetail?> GetSnapshotAsync(
        Guid accountId,
        DateOnly snapshotDate,
        CancellationToken cancellationToken = default)
    {
        var summaries = await GetSummariesAsync(accountId, snapshotDate, snapshotDate, cancellationToken);
        var summary = summaries.SingleOrDefault();
        if (summary is null)
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT market, stock_code, stock_name, quantity, average_purchase_price,
                   current_price, purchase_amount, evaluation_amount, profit_loss,
                   profit_rate_hundredths
            FROM position_snapshots
            WHERE account_id = $accountId AND snapshot_date = $snapshotDate
            ORDER BY stock_code;
            """;
        command.Parameters.AddWithValue("$accountId", accountId.ToString("D"));
        command.Parameters.AddWithValue("$snapshotDate", FormatDate(snapshotDate));

        var positions = new List<PositionSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            positions.Add(new PositionSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                ParseDecimal(reader.GetString(4)),
                ParseDecimal(reader.GetString(5)),
                ParseDecimal(reader.GetString(6)),
                ParseDecimal(reader.GetString(7)),
                ParseDecimal(reader.GetString(8)),
                reader.GetInt32(9)));
        }

        return new AccountSnapshotDetail(summary, positions);
    }

    public async Task UpsertTransactionsAsync(IReadOnlyList<AccountTransaction> transactions, CancellationToken cancellationToken = default)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        foreach (var item in transactions)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO account_transactions (
                    account_id, source_transaction_key, transaction_date, occurred_at_utc,
                    transaction_type, broker_transaction_type, stock_code, stock_name,
                    quantity, unit_price, cash_amount, fee_amount, tax_amount,
                    captured_at_utc, source
                ) VALUES (
                    $accountId, $sourceTransactionKey, $transactionDate, $occurredAtUtc,
                    $transactionType, $brokerTransactionType, $stockCode, $stockName,
                    $quantity, $unitPrice, $cashAmount, $feeAmount, $taxAmount,
                    $capturedAtUtc, $source
                ) ON CONFLICT(account_id, source_transaction_key) DO NOTHING;
                """;
            AddTransactionParameters(command, item);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AccountSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO account_snapshots (
                account_id, snapshot_date, captured_at_utc, cash_balance, cash_balance_captured,
                total_asset_amount,
                total_purchase_amount, total_evaluation_amount, total_profit_loss,
                total_profit_rate_hundredths, source
            ) VALUES (
                $accountId, $snapshotDate, $capturedAtUtc, $cashBalance, $cashBalanceCaptured,
                $totalAssetAmount,
                $totalPurchaseAmount, $totalEvaluationAmount, $totalProfitLoss,
                $totalProfitRateHundredths, $source
            ) ON CONFLICT(account_id, snapshot_date) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$accountId", snapshot.AccountId.ToString("D"));
        command.Parameters.AddWithValue("$snapshotDate", FormatDate(snapshot.SnapshotDate));
        command.Parameters.AddWithValue("$capturedAtUtc", FormatUtc(snapshot.CapturedAtUtc));
        command.Parameters.AddWithValue("$cashBalance", FormatDecimal(snapshot.CashBalance));
        command.Parameters.AddWithValue("$cashBalanceCaptured", snapshot.HasCashBalance ? 1 : 0);
        command.Parameters.AddWithValue("$totalAssetAmount", FormatDecimal(snapshot.TotalAssetAmount));
        command.Parameters.AddWithValue("$totalPurchaseAmount", FormatDecimal(snapshot.TotalPurchaseAmount));
        command.Parameters.AddWithValue("$totalEvaluationAmount", FormatDecimal(snapshot.TotalEvaluationAmount));
        command.Parameters.AddWithValue("$totalProfitLoss", FormatDecimal(snapshot.TotalProfitLoss));
        command.Parameters.AddWithValue("$totalProfitRateHundredths", snapshot.TotalProfitRateHundredths);
        command.Parameters.AddWithValue("$source", snapshot.Source);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task InsertPositionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AccountSnapshot snapshot,
        PositionSnapshot position,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO position_snapshots (
                account_id, snapshot_date, market, stock_code, stock_name, quantity,
                average_purchase_price, current_price, purchase_amount, evaluation_amount,
                profit_loss, profit_rate_hundredths
            ) VALUES (
                $accountId, $snapshotDate, $market, $stockCode, $stockName, $quantity,
                $averagePurchasePrice, $currentPrice, $purchaseAmount, $evaluationAmount,
                $profitLoss, $profitRateHundredths
            );
            """;
        command.Parameters.AddWithValue("$accountId", snapshot.AccountId.ToString("D"));
        command.Parameters.AddWithValue("$snapshotDate", FormatDate(snapshot.SnapshotDate));
        command.Parameters.AddWithValue("$market", position.Market);
        command.Parameters.AddWithValue("$stockCode", position.StockCode);
        command.Parameters.AddWithValue("$stockName", position.StockName);
        command.Parameters.AddWithValue("$quantity", position.Quantity);
        command.Parameters.AddWithValue("$averagePurchasePrice", FormatDecimal(position.AveragePurchasePrice));
        command.Parameters.AddWithValue("$currentPrice", FormatDecimal(position.CurrentPrice));
        command.Parameters.AddWithValue("$purchaseAmount", FormatDecimal(position.PurchaseAmount));
        command.Parameters.AddWithValue("$evaluationAmount", FormatDecimal(position.EvaluationAmount));
        command.Parameters.AddWithValue("$profitLoss", FormatDecimal(position.ProfitLoss));
        command.Parameters.AddWithValue("$profitRateHundredths", position.ProfitRateHundredths);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddTransactionParameters(SqliteCommand command, AccountTransaction item)
    {
        command.Parameters.AddWithValue("$accountId", item.AccountId.ToString("D"));
        command.Parameters.AddWithValue("$sourceTransactionKey", item.SourceTransactionKey);
        command.Parameters.AddWithValue("$transactionDate", FormatDate(item.TransactionDate));
        command.Parameters.AddWithValue("$occurredAtUtc", item.OccurredAtUtc is { } occurredAtUtc ? FormatUtc(occurredAtUtc) : DBNull.Value);
        command.Parameters.AddWithValue("$transactionType", (int)item.TransactionType);
        command.Parameters.AddWithValue("$brokerTransactionType", item.BrokerTransactionType);
        command.Parameters.AddWithValue("$stockCode", (object?)item.StockCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$stockName", (object?)item.StockName ?? DBNull.Value);
        command.Parameters.AddWithValue("$quantity", (object?)item.Quantity ?? DBNull.Value);
        command.Parameters.AddWithValue("$unitPrice", item.UnitPrice is { } unitPrice ? FormatDecimal(unitPrice) : DBNull.Value);
        command.Parameters.AddWithValue("$cashAmount", FormatDecimal(item.CashAmount));
        command.Parameters.AddWithValue("$feeAmount", FormatDecimal(item.FeeAmount));
        command.Parameters.AddWithValue("$taxAmount", FormatDecimal(item.TaxAmount));
        command.Parameters.AddWithValue("$capturedAtUtc", FormatUtc(item.CapturedAtUtc));
        command.Parameters.AddWithValue("$source", item.Source);
    }

    private static string FormatDate(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static string FormatUtc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static async Task EnsureCashBalanceCapturedColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var columnCommand = connection.CreateCommand();
        columnCommand.CommandText = "PRAGMA table_info(account_snapshots);";
        await using var reader = await columnCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), "cash_balance_captured", StringComparison.Ordinal))
            {
                return;
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = """
            ALTER TABLE account_snapshots
            ADD COLUMN cash_balance_captured INTEGER NOT NULL DEFAULT 0;
            """;
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
