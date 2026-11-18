namespace AssetPilot.Backtest.Analysis;

public sealed record BacktestAnalysisOptions
{
    public BacktestAnalysisOptions(int fixedObservationTradingDays = 5)
    {
        if (fixedObservationTradingDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedObservationTradingDays));
        }

        FixedObservationTradingDays = fixedObservationTradingDays;
    }

    public int FixedObservationTradingDays { get; }
}
