namespace AssetPilot.Core.Transactions;

public sealed record AccountTransaction(
    Guid AccountId,
    string SourceTransactionKey,
    DateOnly TransactionDate,
    DateTimeOffset? OccurredAtUtc,
    TransactionType TransactionType,
    string BrokerTransactionType,
    string? StockCode,
    string? StockName,
    long? Quantity,
    decimal? UnitPrice,
    decimal CashAmount,
    decimal FeeAmount,
    decimal TaxAmount,
    DateTimeOffset CapturedAtUtc,
    string Source);
