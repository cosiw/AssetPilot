using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Strategy;

public sealed class SwingAtrStrategy(StrategyOptions options) : ITradingStrategy, IEntrySignalDiagnosticSource
{
    public string Key => "swing-atr";

    public string DisplayName => "ATR 단일 진입";

    public EntryPlan? EvaluateAtClose(IReadOnlyList<DailyBar> dailyBars)
    {
        if (dailyBars.Count < RequiredBarCount || EvaluateTrendFilters(dailyBars) is not null)
        {
            return null;
        }

        var signalBar = dailyBars[^1];
        var sma = dailyBars.TakeLast(options.SmaPeriod).Average(bar => bar.Close);

        var atr = CalculateAtr(dailyBars.TakeLast(options.AtrPeriod).ToArray());
        return new EntryPlan(
            signalBar.Date,
            signalBar.Close - (atr * options.BuyBandAtrMultiplier),
            signalBar.Close + (atr * options.BuyBandAtrMultiplier),
            atr);
    }

    public ExitPlan CreateExitPlan(decimal entryPrice, decimal atr) => new(
        entryPrice + (atr * options.TargetAtrMultiplier),
        entryPrice - (atr * options.StopAtrMultiplier));

    public EntrySignalRejection? ExplainNoEntry(IReadOnlyList<DailyBar> dailyBars)
    {
        if (dailyBars.Count < RequiredBarCount)
        {
            return null;
        }

        return EvaluateTrendFilters(dailyBars);
    }

    private int RequiredBarCount => Math.Max(
        options.SmaPeriod + options.SmaSlopeLookbackDays,
        Math.Max(options.ShortSmaPeriod, options.AtrPeriod));

    private EntrySignalRejection? EvaluateTrendFilters(IReadOnlyList<DailyBar> dailyBars)
    {
        var signalBar = dailyBars[^1];
        var longSma = dailyBars.TakeLast(options.SmaPeriod).Average(bar => bar.Close);
        var priorLongSma = dailyBars
            .Skip(dailyBars.Count - options.SmaPeriod - options.SmaSlopeLookbackDays)
            .Take(options.SmaPeriod)
            .Average(bar => bar.Close);
        if (longSma <= priorLongSma)
        {
            return new EntrySignalRejection("LongSmaSlopeNotPositive", "장기 이동평균의 기울기가 양수가 아닙니다.");
        }

        var requiredClose = longSma * (1m + options.MinimumSmaDistancePercent / 100m);
        if (signalBar.Close < requiredClose)
        {
            return new EntrySignalRejection("MinimumSmaDistanceNotMet", $"종가가 장기 이동평균보다 {options.MinimumSmaDistancePercent:N2}% 이상 높지 않습니다.");
        }

        var shortSma = dailyBars.TakeLast(options.ShortSmaPeriod).Average(bar => bar.Close);
        return shortSma <= longSma
            ? new EntrySignalRejection("ShortSmaNotAboveLongSma", "단기 이동평균이 장기 이동평균보다 높지 않습니다.")
            : null;
    }

    private static decimal CalculateAtr(IReadOnlyList<DailyBar> bars)
    {
        var totalTrueRange = 0m;
        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var previousClose = index == 0 ? bar.Close : bars[index - 1].Close;
            totalTrueRange += Math.Max(
                bar.High - bar.Low,
                Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));
        }

        return totalTrueRange / bars.Count;
    }
}
