using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Strategy;

public interface IEntrySignalDiagnosticSource
{
    EntrySignalRejection? ExplainNoEntry(IReadOnlyList<DailyBar> dailyBars);
}

public sealed record EntrySignalRejection(string Code, string Message);
