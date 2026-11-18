using System.Globalization;
using System.Text.Json;
using AssetPilot.Backtest.Analysis;
using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Storage;
using Microsoft.Data.Sqlite;

namespace AssetPilot.Infrastructure.Backtests;

public sealed class SqliteBacktestRunStore(string connectionString) : IBacktestRunStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS backtest_runs (id TEXT PRIMARY KEY, executed_at TEXT NOT NULL, strategy_key TEXT NOT NULL, strategy_name TEXT NOT NULL, options_json TEXT NOT NULL, symbol TEXT NOT NULL, starting_cash TEXT NOT NULL, ending_cash TEXT NOT NULL, total_profit TEXT NOT NULL, return_rate TEXT NOT NULL, experiment_id TEXT NULL);
            CREATE TABLE IF NOT EXISTS backtest_executions (run_id TEXT NOT NULL, executed_at TEXT NOT NULL, side INTEGER NOT NULL, price TEXT NOT NULL, quantity TEXT NOT NULL, realized_profit TEXT NULL, reason INTEGER NULL, FOREIGN KEY(run_id) REFERENCES backtest_runs(id));
            CREATE TABLE IF NOT EXISTS backtest_diagnostics (run_id TEXT NOT NULL, code TEXT NOT NULL, trade_date TEXT NOT NULL, message TEXT NOT NULL, FOREIGN KEY(run_id) REFERENCES backtest_runs(id));
            CREATE TABLE IF NOT EXISTS backtest_position_snapshots (run_id TEXT NOT NULL, execution_id TEXT NOT NULL, execution_sequence INTEGER NOT NULL, recorded_at TEXT NOT NULL, cash TEXT NOT NULL, quantity TEXT NOT NULL, average_entry_price TEXT NOT NULL, position_cost TEXT NOT NULL, realized_profit_total TEXT NOT NULL, PRIMARY KEY (run_id, execution_id), FOREIGN KEY(run_id) REFERENCES backtest_runs(id));
            CREATE TABLE IF NOT EXISTS backtest_position_cycles (run_id TEXT NOT NULL, cycle_id TEXT NOT NULL, opening_execution_id TEXT NOT NULL, closing_execution_id TEXT NOT NULL, opened_at TEXT NOT NULL, closed_at TEXT NOT NULL, bought_quantity TEXT NOT NULL, sold_quantity TEXT NOT NULL, average_entry_price TEXT NOT NULL, average_exit_price TEXT NOT NULL, realized_profit TEXT NOT NULL, return_rate TEXT NOT NULL, closing_reason INTEGER NULL, PRIMARY KEY (run_id, cycle_id), FOREIGN KEY(run_id) REFERENCES backtest_runs(id));
            CREATE TABLE IF NOT EXISTS backtest_excursions (run_id TEXT NOT NULL, cycle_id TEXT NOT NULL, holding_mfe_amount TEXT NOT NULL, holding_mfe_rate TEXT NOT NULL, holding_mae_amount TEXT NOT NULL, holding_mae_rate TEXT NOT NULL, fixed_mfe_amount TEXT NOT NULL, fixed_mfe_rate TEXT NOT NULL, fixed_mae_amount TEXT NOT NULL, fixed_mae_rate TEXT NOT NULL, post_exit_mfe_rate TEXT NULL, post_exit_mae_rate TEXT NULL, fixed_observation_trading_days INTEGER NOT NULL, PRIMARY KEY (run_id, cycle_id), FOREIGN KEY(run_id) REFERENCES backtest_runs(id));
            CREATE TABLE IF NOT EXISTS backtest_equity_points (run_id TEXT NOT NULL, recorded_at TEXT NOT NULL, cash TEXT NOT NULL, position_quantity TEXT NOT NULL, average_entry_price TEXT NOT NULL, mark_price TEXT NOT NULL, position_value TEXT NOT NULL, total_equity TEXT NOT NULL, cumulative_return TEXT NOT NULL, peak_equity TEXT NOT NULL, drawdown_amount TEXT NOT NULL, drawdown_rate TEXT NOT NULL, PRIMARY KEY (run_id, recorded_at), FOREIGN KEY(run_id) REFERENCES backtest_runs(id));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AddExecutionColumnAsync(connection, "execution_id TEXT NULL", cancellationToken);
        await AddExecutionColumnAsync(connection, "execution_sequence INTEGER NULL", cancellationToken);
        await AddExecutionColumnAsync(connection, "gross_amount TEXT NULL", cancellationToken);
        await AddExecutionColumnAsync(connection, "fee TEXT NULL", cancellationToken);
        await AddExecutionColumnAsync(connection, "tax TEXT NULL", cancellationToken);
        await AddRunColumnAsync(connection, "data_start_date TEXT NULL", cancellationToken);
        await AddRunColumnAsync(connection, "data_end_date TEXT NULL", cancellationToken);
        await AddRunColumnAsync(connection, "analysis_options_json TEXT NULL", cancellationToken);
        await AddRunColumnAsync(connection, "experiment_id TEXT NULL", cancellationToken);
    }

    public async Task SaveAsync(BacktestRun run, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO backtest_runs (id,executed_at,strategy_key,strategy_name,options_json,symbol,starting_cash,ending_cash,total_profit,return_rate,data_start_date,data_end_date,analysis_options_json,experiment_id) VALUES ($id,$at,$key,$name,$options,$symbol,$start,$end,$profit,$return,$dataStart,$dataEnd,$analysisOptions,$experimentId)";
            command.Parameters.AddWithValue("$id", run.Id.ToString()); command.Parameters.AddWithValue("$at", run.ExecutedAt.ToString("O")); command.Parameters.AddWithValue("$key", run.StrategyKey); command.Parameters.AddWithValue("$name", run.StrategyName); command.Parameters.AddWithValue("$options", run.StrategyOptionsJson); command.Parameters.AddWithValue("$symbol", run.Symbol); command.Parameters.AddWithValue("$start", D(run.StartingCash)); command.Parameters.AddWithValue("$end", D(run.EndingCash)); command.Parameters.AddWithValue("$profit", D(run.TotalProfit)); command.Parameters.AddWithValue("$return", D(run.ReturnRate)); command.Parameters.AddWithValue("$dataStart", run.DataStartDate is { } dataStart ? dataStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value); command.Parameters.AddWithValue("$dataEnd", run.DataEndDate is { } dataEnd ? dataEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value); command.Parameters.AddWithValue("$analysisOptions", run.AnalysisOptions is null ? DBNull.Value : JsonSerializer.Serialize(run.AnalysisOptions)); command.Parameters.AddWithValue("$experimentId", run.ExperimentId is { } experimentId ? experimentId.ToString() : DBNull.Value); await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var e in run.Executions) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO backtest_executions (run_id,executed_at,side,price,quantity,realized_profit,reason,execution_id,execution_sequence,gross_amount,fee,tax) VALUES ($id,$at,$side,$price,$qty,$profit,$reason,$executionId,$sequence,$gross,$fee,$tax)"; command.Parameters.AddWithValue("$id",run.Id.ToString()); command.Parameters.AddWithValue("$at",e.ExecutedAt.ToString("O")); command.Parameters.AddWithValue("$side",(int)e.Side); command.Parameters.AddWithValue("$price",D(e.Price)); command.Parameters.AddWithValue("$qty",D(e.Quantity)); command.Parameters.AddWithValue("$profit",e.RealizedProfit is { } p ? D(p) : DBNull.Value); command.Parameters.AddWithValue("$reason",e.Reason is { } reason ? (int)reason : DBNull.Value); command.Parameters.AddWithValue("$executionId", e.ExecutionId == Guid.Empty ? DBNull.Value : e.ExecutionId.ToString()); command.Parameters.AddWithValue("$sequence", e.Sequence == 0 ? DBNull.Value : e.Sequence); command.Parameters.AddWithValue("$gross", D(e.GrossAmount)); command.Parameters.AddWithValue("$fee", D(e.Fee)); command.Parameters.AddWithValue("$tax", D(e.Tax)); await command.ExecuteNonQueryAsync(cancellationToken); }
        foreach (var diagnostic in run.DiagnosticsOrEmpty) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO backtest_diagnostics VALUES ($id,$code,$date,$message)"; command.Parameters.AddWithValue("$id", run.Id.ToString()); command.Parameters.AddWithValue("$code", diagnostic.Code); command.Parameters.AddWithValue("$date", diagnostic.TradeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$message", diagnostic.Message); await command.ExecuteNonQueryAsync(cancellationToken); }
        foreach (var snapshot in run.PositionSnapshotsOrEmpty) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO backtest_position_snapshots VALUES ($run,$execution,$sequence,$at,$cash,$quantity,$average,$cost,$realized)"; command.Parameters.AddWithValue("$run", run.Id.ToString()); command.Parameters.AddWithValue("$execution", snapshot.ExecutionId.ToString()); command.Parameters.AddWithValue("$sequence", snapshot.ExecutionSequence); command.Parameters.AddWithValue("$at", snapshot.RecordedAt.ToString("O")); command.Parameters.AddWithValue("$cash", D(snapshot.Cash)); command.Parameters.AddWithValue("$quantity", D(snapshot.Quantity)); command.Parameters.AddWithValue("$average", D(snapshot.AverageEntryPrice)); command.Parameters.AddWithValue("$cost", D(snapshot.PositionCost)); command.Parameters.AddWithValue("$realized", D(snapshot.RealizedProfitTotal)); await command.ExecuteNonQueryAsync(cancellationToken); }
        foreach (var cycle in run.CompletedPositionCyclesOrEmpty) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO backtest_position_cycles VALUES ($run,$cycle,$opening,$closing,$openedAt,$closedAt,$bought,$sold,$entry,$exit,$profit,$return,$reason)"; command.Parameters.AddWithValue("$run", run.Id.ToString()); command.Parameters.AddWithValue("$cycle", cycle.CycleId.ToString()); command.Parameters.AddWithValue("$opening", cycle.OpeningExecutionId.ToString()); command.Parameters.AddWithValue("$closing", cycle.ClosingExecutionId.ToString()); command.Parameters.AddWithValue("$openedAt", cycle.OpenedAt.ToString("O")); command.Parameters.AddWithValue("$closedAt", cycle.ClosedAt.ToString("O")); command.Parameters.AddWithValue("$bought", D(cycle.BoughtQuantity)); command.Parameters.AddWithValue("$sold", D(cycle.SoldQuantity)); command.Parameters.AddWithValue("$entry", D(cycle.AverageEntryPrice)); command.Parameters.AddWithValue("$exit", D(cycle.AverageExitPrice)); command.Parameters.AddWithValue("$profit", D(cycle.RealizedProfit)); command.Parameters.AddWithValue("$return", D(cycle.ReturnRate)); command.Parameters.AddWithValue("$reason", cycle.ClosingReason is { } reason ? (int)reason : DBNull.Value); await command.ExecuteNonQueryAsync(cancellationToken); }
        foreach (var excursion in run.Excursions) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO backtest_excursions VALUES ($run,$cycle,$holdingMfeAmount,$holdingMfeRate,$holdingMaeAmount,$holdingMaeRate,$fixedMfeAmount,$fixedMfeRate,$fixedMaeAmount,$fixedMaeRate,$postExitMfeRate,$postExitMaeRate,$days)"; command.Parameters.AddWithValue("$run", run.Id.ToString()); command.Parameters.AddWithValue("$cycle", excursion.CycleId.ToString()); command.Parameters.AddWithValue("$holdingMfeAmount", D(excursion.HoldingMfeAmount)); command.Parameters.AddWithValue("$holdingMfeRate", D(excursion.HoldingMfeRate)); command.Parameters.AddWithValue("$holdingMaeAmount", D(excursion.HoldingMaeAmount)); command.Parameters.AddWithValue("$holdingMaeRate", D(excursion.HoldingMaeRate)); command.Parameters.AddWithValue("$fixedMfeAmount", D(excursion.FixedMfeAmount)); command.Parameters.AddWithValue("$fixedMfeRate", D(excursion.FixedMfeRate)); command.Parameters.AddWithValue("$fixedMaeAmount", D(excursion.FixedMaeAmount)); command.Parameters.AddWithValue("$fixedMaeRate", D(excursion.FixedMaeRate)); command.Parameters.AddWithValue("$postExitMfeRate", excursion.PostExitMfeRate is { } postExitMfeRate ? D(postExitMfeRate) : DBNull.Value); command.Parameters.AddWithValue("$postExitMaeRate", excursion.PostExitMaeRate is { } postExitMaeRate ? D(postExitMaeRate) : DBNull.Value); command.Parameters.AddWithValue("$days", excursion.FixedObservationTradingDays); await command.ExecuteNonQueryAsync(cancellationToken); }
        foreach (var point in run.EquityCurve?.Points ?? []) { await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "INSERT INTO backtest_equity_points VALUES ($run,$at,$cash,$quantity,$average,$mark,$value,$equity,$return,$peak,$drawdownAmount,$drawdownRate)"; command.Parameters.AddWithValue("$run", run.Id.ToString()); command.Parameters.AddWithValue("$at", point.DateTime.ToString("O")); command.Parameters.AddWithValue("$cash", D(point.Cash)); command.Parameters.AddWithValue("$quantity", D(point.PositionQuantity)); command.Parameters.AddWithValue("$average", D(point.AverageEntryPrice)); command.Parameters.AddWithValue("$mark", D(point.MarkPrice)); command.Parameters.AddWithValue("$value", D(point.PositionValue)); command.Parameters.AddWithValue("$equity", D(point.TotalEquity)); command.Parameters.AddWithValue("$return", D(point.CumulativeReturn)); command.Parameters.AddWithValue("$peak", D(point.PeakEquity)); command.Parameters.AddWithValue("$drawdownAmount", D(point.DrawdownAmount)); command.Parameters.AddWithValue("$drawdownRate", D(point.DrawdownRate)); await command.ExecuteNonQueryAsync(cancellationToken); }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<BacktestRun?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT executed_at,strategy_key,strategy_name,options_json,symbol,starting_cash,ending_cash,total_profit,return_rate,data_start_date,data_end_date,analysis_options_json,experiment_id FROM backtest_runs WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var executedAt = DateTimeOffset.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind);
        var strategyKey = reader.GetString(1); var strategyName = reader.GetString(2); var options = reader.GetString(3); var symbol = reader.GetString(4);
        var startingCash = P(reader.GetString(5)); var endingCash = P(reader.GetString(6)); var totalProfit = P(reader.GetString(7)); var returnRate = P(reader.GetString(8));
        DateOnly? dataStartDate = reader.IsDBNull(9) ? null : DateOnly.ParseExact(reader.GetString(9), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly? dataEndDate = reader.IsDBNull(10) ? null : DateOnly.ParseExact(reader.GetString(10), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var analysisOptions = reader.IsDBNull(11) ? null : JsonSerializer.Deserialize<BacktestAnalysisOptions>(reader.GetString(11));
        Guid? experimentId = reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12));
        var executions = new List<BacktestExecution>();
        await reader.DisposeAsync();
        await using var executionCommand = connection.CreateCommand();
        executionCommand.CommandText = "SELECT executed_at,side,price,quantity,realized_profit,reason,execution_id,execution_sequence,gross_amount,fee,tax FROM backtest_executions WHERE run_id=$id ORDER BY execution_sequence IS NULL,execution_sequence,executed_at,rowid";
        executionCommand.Parameters.AddWithValue("$id", id.ToString());
        await using var executionReader = await executionCommand.ExecuteReaderAsync(cancellationToken);
        while (await executionReader.ReadAsync(cancellationToken))
        {
            var price = P(executionReader.GetString(2));
            var quantity = P(executionReader.GetString(3));
            executions.Add(new BacktestExecution(
                executionReader.IsDBNull(6) ? Guid.Empty : Guid.Parse(executionReader.GetString(6)),
                executionReader.IsDBNull(7) ? executions.Count + 1 : executionReader.GetInt32(7),
                DateTime.Parse(executionReader.GetString(0), null, DateTimeStyles.RoundtripKind),
                (ExecutionSide)executionReader.GetInt32(1), price, quantity,
                executionReader.IsDBNull(8) ? price * quantity : P(executionReader.GetString(8)),
                executionReader.IsDBNull(9) ? 0m : P(executionReader.GetString(9)),
                executionReader.IsDBNull(10) ? 0m : P(executionReader.GetString(10)),
                executionReader.IsDBNull(4) ? null : P(executionReader.GetString(4)),
                executionReader.IsDBNull(5) ? null : (ExitReason)executionReader.GetInt32(5)));
        }
        var diagnostics = new List<BacktestDiagnostic>();
        await using var diagnosticCommand = connection.CreateCommand();
        diagnosticCommand.CommandText = "SELECT code,trade_date,message FROM backtest_diagnostics WHERE run_id=$id ORDER BY trade_date";
        diagnosticCommand.Parameters.AddWithValue("$id", id.ToString());
        await using var diagnosticReader = await diagnosticCommand.ExecuteReaderAsync(cancellationToken);
        while (await diagnosticReader.ReadAsync(cancellationToken)) diagnostics.Add(new BacktestDiagnostic(diagnosticReader.GetString(0), DateOnly.ParseExact(diagnosticReader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), diagnosticReader.GetString(2)));
        var snapshots = new List<BacktestPositionSnapshot>();
        await using var snapshotCommand = connection.CreateCommand();
        snapshotCommand.CommandText = "SELECT execution_id,execution_sequence,recorded_at,cash,quantity,average_entry_price,position_cost,realized_profit_total FROM backtest_position_snapshots WHERE run_id=$id ORDER BY execution_sequence";
        snapshotCommand.Parameters.AddWithValue("$id", id.ToString());
        await using var snapshotReader = await snapshotCommand.ExecuteReaderAsync(cancellationToken);
        while (await snapshotReader.ReadAsync(cancellationToken)) snapshots.Add(new BacktestPositionSnapshot(Guid.Parse(snapshotReader.GetString(0)), snapshotReader.GetInt32(1), DateTime.Parse(snapshotReader.GetString(2), null, DateTimeStyles.RoundtripKind), P(snapshotReader.GetString(3)), P(snapshotReader.GetString(4)), P(snapshotReader.GetString(5)), P(snapshotReader.GetString(6)), P(snapshotReader.GetString(7))));
        var cycles = new List<BacktestPositionCycle>();
        await using var cycleCommand = connection.CreateCommand();
        cycleCommand.CommandText = "SELECT cycle_id,opening_execution_id,closing_execution_id,opened_at,closed_at,bought_quantity,sold_quantity,average_entry_price,average_exit_price,realized_profit,return_rate,closing_reason FROM backtest_position_cycles WHERE run_id=$id ORDER BY opened_at";
        cycleCommand.Parameters.AddWithValue("$id", id.ToString());
        await using var cycleReader = await cycleCommand.ExecuteReaderAsync(cancellationToken);
        while (await cycleReader.ReadAsync(cancellationToken)) cycles.Add(new BacktestPositionCycle(Guid.Parse(cycleReader.GetString(0)), Guid.Parse(cycleReader.GetString(1)), Guid.Parse(cycleReader.GetString(2)), DateTime.Parse(cycleReader.GetString(3), null, DateTimeStyles.RoundtripKind), DateTime.Parse(cycleReader.GetString(4), null, DateTimeStyles.RoundtripKind), P(cycleReader.GetString(5)), P(cycleReader.GetString(6)), P(cycleReader.GetString(7)), P(cycleReader.GetString(8)), P(cycleReader.GetString(9)), P(cycleReader.GetString(10)), cycleReader.IsDBNull(11) ? null : (ExitReason)cycleReader.GetInt32(11)));
        var excursions = new List<BacktestExcursion>();
        await using var excursionCommand = connection.CreateCommand();
        excursionCommand.CommandText = "SELECT cycle_id,holding_mfe_amount,holding_mfe_rate,holding_mae_amount,holding_mae_rate,fixed_mfe_amount,fixed_mfe_rate,fixed_mae_amount,fixed_mae_rate,post_exit_mfe_rate,post_exit_mae_rate,fixed_observation_trading_days FROM backtest_excursions WHERE run_id=$id ORDER BY cycle_id";
        excursionCommand.Parameters.AddWithValue("$id", id.ToString());
        await using var excursionReader = await excursionCommand.ExecuteReaderAsync(cancellationToken);
        while (await excursionReader.ReadAsync(cancellationToken)) excursions.Add(new BacktestExcursion(Guid.Parse(excursionReader.GetString(0)), P(excursionReader.GetString(1)), P(excursionReader.GetString(2)), P(excursionReader.GetString(3)), P(excursionReader.GetString(4)), P(excursionReader.GetString(5)), P(excursionReader.GetString(6)), P(excursionReader.GetString(7)), P(excursionReader.GetString(8)), excursionReader.IsDBNull(9) ? null : P(excursionReader.GetString(9)), excursionReader.IsDBNull(10) ? null : P(excursionReader.GetString(10)), excursionReader.GetInt32(11)));
        var equityPoints = new List<BacktestEquityPoint>();
        await using var equityPointCommand = connection.CreateCommand();
        equityPointCommand.CommandText = "SELECT recorded_at,cash,position_quantity,average_entry_price,mark_price,position_value,total_equity,cumulative_return,peak_equity,drawdown_amount,drawdown_rate FROM backtest_equity_points WHERE run_id=$id ORDER BY recorded_at";
        equityPointCommand.Parameters.AddWithValue("$id", id.ToString());
        await using var equityPointReader = await equityPointCommand.ExecuteReaderAsync(cancellationToken);
        while (await equityPointReader.ReadAsync(cancellationToken)) equityPoints.Add(new BacktestEquityPoint(DateTime.Parse(equityPointReader.GetString(0), null, DateTimeStyles.RoundtripKind), P(equityPointReader.GetString(1)), P(equityPointReader.GetString(2)), P(equityPointReader.GetString(3)), P(equityPointReader.GetString(4)), P(equityPointReader.GetString(5)), P(equityPointReader.GetString(6)), P(equityPointReader.GetString(7)), P(equityPointReader.GetString(8)), P(equityPointReader.GetString(9)), P(equityPointReader.GetString(10))));
        var equityCurve = CreateEquityCurve(equityPoints);
        var performanceReport = dataStartDate is { } startDate && dataEndDate is { } endDate
            ? BacktestPerformanceCalculator.Calculate(startingCash, startDate, endDate, cycles, excursions)
            : null;
        return new BacktestRun(id, executedAt, strategyKey, strategyName, options, symbol, startingCash, endingCash, totalProfit, returnRate, executions, diagnostics, snapshots, cycles, dataStartDate, dataEndDate, analysisOptions, excursions, performanceReport, equityCurve, experimentId);
    }

    public async Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,executed_at,strategy_name,symbol,total_profit,return_rate FROM backtest_runs ORDER BY executed_at DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var summaries = new List<BacktestRunSummary>();
        while (await reader.ReadAsync(cancellationToken)) summaries.Add(new BacktestRunSummary(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind), reader.GetString(2), reader.GetString(3), P(reader.GetString(4)), P(reader.GetString(5))));
        return summaries;
    }

    public async Task<IReadOnlyList<BacktestRunSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return [];
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var names = new List<string>();
        foreach (var id in ids)
        {
            var name = $"$id{names.Count}";
            names.Add(name);
            command.Parameters.AddWithValue(name, id.ToString());
        }
        command.CommandText = $"SELECT id,executed_at,strategy_name,symbol,total_profit,return_rate FROM backtest_runs WHERE id IN ({string.Join(',', names)}) ORDER BY executed_at DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var summaries = new List<BacktestRunSummary>();
        while (await reader.ReadAsync(cancellationToken)) summaries.Add(new BacktestRunSummary(Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind), reader.GetString(2), reader.GetString(3), P(reader.GetString(4)), P(reader.GetString(5))));
        return summaries;
    }
    private async Task<SqliteConnection> OpenAsync(CancellationToken token) { var c = new SqliteConnection(connectionString); await c.OpenAsync(token); return c; }
    private static async Task AddExecutionColumnAsync(SqliteConnection connection, string definition, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE backtest_executions ADD COLUMN {definition}";
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)) { }
    }
    private static async Task AddRunColumnAsync(SqliteConnection connection, string definition, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE backtest_runs ADD COLUMN {definition}";
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqliteException exception) when (exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)) { }
    }
    private static string D(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static decimal P(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static BacktestEquityCurve CreateEquityCurve(IReadOnlyList<BacktestEquityPoint> points)
    {
        if (points.Count == 0) return new BacktestEquityCurve([], 0m, 0m, null, null);

        var trough = points.OrderBy(point => point.DrawdownAmount).ThenBy(point => point.DateTime).First();
        var peak = points
            .Where(point => point.DateTime <= trough.DateTime && point.TotalEquity == point.PeakEquity)
            .OrderBy(point => point.DateTime)
            .LastOrDefault();
        return new BacktestEquityCurve(points, -trough.DrawdownAmount, -trough.DrawdownRate, peak?.DateTime, trough.DrawdownAmount < 0m ? trough.DateTime : null);
    }
}
