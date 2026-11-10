namespace AssetPilot.Core.Snapshots;

public sealed record PositionSnapshot(
    string Market,
    string StockCode,
    string StockName,
    long Quantity,
    decimal AveragePurchasePrice,
    decimal CurrentPrice,
    decimal PurchaseAmount,
    decimal EvaluationAmount,
    decimal ProfitLoss,
    int ProfitRateHundredths);
