using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Strategy;

public interface ITradingStrategy
{
    string Key { get; }

    string DisplayName { get; }

    EntryPlan? EvaluateAtClose(IReadOnlyList<DailyBar> dailyBars);

    ExitPlan CreateExitPlan(decimal entryPrice, decimal atr);
}
