namespace AssetPilot.Core.Snapshots;

public sealed record AccountSnapshot(
    Guid AccountId,
    DateOnly SnapshotDate,
    DateTimeOffset CapturedAtUtc,
    decimal CashBalance,
    bool HasCashBalance,
    decimal TotalAssetAmount,
    decimal TotalPurchaseAmount,
    decimal TotalEvaluationAmount,
    decimal TotalProfitLoss,
    int TotalProfitRateHundredths,
    IReadOnlyList<PositionSnapshot> Positions,
    string Source);
