namespace AssetPilot.Core.Portfolio;

public sealed record Portfolio(
    decimal TotalPurchaseAmount,
    decimal TotalEvaluationAmount,
    decimal TotalProfitLoss,
    decimal TotalProfitRate,
    IReadOnlyList<PortfolioPosition> Positions,
    decimal CashBalance = 0,
    decimal TotalAssetAmount = 0);
