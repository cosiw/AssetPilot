namespace AssetPilot.Core.Snapshots;

public sealed record AccountSnapshotSummary(
    DateOnly SnapshotDate,
    DateTimeOffset CapturedAtUtc,
    decimal CashBalance,
    bool HasCashBalance,
    decimal TotalAssetAmount,
    decimal TotalPurchaseAmount,
    decimal TotalEvaluationAmount,
    decimal TotalProfitLoss,
    int TotalProfitRateHundredths);
