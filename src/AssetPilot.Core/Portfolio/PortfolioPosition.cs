namespace AssetPilot.Core.Portfolio;

public sealed record PortfolioPosition(
    string StockCode,
    string StockName,
    int Quantity,
    decimal AveragePrice,
    decimal CurrentPrice,
    decimal PurchaseAmount,
    decimal EvaluationAmount,
    decimal EvaluationProfitLoss,
    decimal ProfitRate);
