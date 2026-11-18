using AssetPilot.Backtest.MarketData;
using AssetPilot.Backtest.Execution;

namespace AssetPilot.Backtest.Strategy;

public sealed class WalkForwardParameterSelector
{
    private const int MinimumTrainingDays = 3 * 252;

    public ParameterRecommendation Select(IReadOnlyList<DailyBar> dailyBars, IReadOnlyList<FiveMinuteBar> fiveMinuteBars, DateOnly decisionDate)
    {
        if (dailyBars.Count(bar => bar.Date <= decisionDate) < MinimumTrainingDays)
        {
            return new(ParameterDecisionStatus.InsufficientHistory, decisionDate, decisionDate.AddDays(1), null, "At least three years of daily history are required.");
        }

        var validationStart = decisionDate.AddMonths(-6);
        var intraday = fiveMinuteBars.Where(bar => DateOnly.FromDateTime(bar.Timestamp) <= decisionDate).ToArray();
        if (intraday.Length == 0 || intraday.Min(bar => DateOnly.FromDateTime(bar.Timestamp)) > validationStart)
        {
            return new(ParameterDecisionStatus.InsufficientHistory, decisionDate, decisionDate.AddDays(1), null, "Five-minute validation history is required before a recommendation can be published.");
        }

        var daily = dailyBars.Where(bar => bar.Date <= decisionDate).OrderBy(bar => bar.Date).ToArray();
        var candidates = new[]
        {
            new StrategyOptions { SmaPeriod = 40, SmaSlopeLookbackDays = 20, MinimumSmaDistancePercent = 1m, ShortSmaPeriod = 5, AtrPeriod = 3, BuyBandAtrMultiplier = .25m, TargetAtrMultiplier = 4m, StopAtrMultiplier = 1.5m, MaxHoldingDays = 5 },
            new StrategyOptions(),
            new StrategyOptions { SmaPeriod = 60, SmaSlopeLookbackDays = 10, MinimumSmaDistancePercent = 1m, ShortSmaPeriod = 10, AtrPeriod = 5, BuyBandAtrMultiplier = .5m, TargetAtrMultiplier = 3m, StopAtrMultiplier = 1m, MaxHoldingDays = 5 }
        };
        var ranked = candidates.Select(options => Evaluate(options, daily, intraday, validationStart)).Where(result => result.Metrics.CompletedTrades >= 20)
            .OrderByDescending(result => result.Metrics.ReturnRate).ThenByDescending(result => result.Metrics.ProfitFactor).ThenBy(result => result.Metrics.MaxDrawdown).FirstOrDefault();
        return ranked is null
            ? new(ParameterDecisionStatus.InsufficientHistory, decisionDate, decisionDate.AddDays(1), null, "No candidate completed at least 20 validation trades.")
            : new ParameterRecommendation(ParameterDecisionStatus.PendingApproval, decisionDate, decisionDate.AddDays(1), ranked.Options, $"Validation return={ranked.Metrics.ReturnRate:P2}; trades={ranked.Metrics.CompletedTrades}; profit factor={ranked.Metrics.ProfitFactor:F2}.") { CompletedTrades = ranked.Metrics.CompletedTrades, ValidationReturnRate = ranked.Metrics.ReturnRate, ProfitFactor = ranked.Metrics.ProfitFactor, MaxDrawdown = ranked.Metrics.MaxDrawdown / 10_000_000m };
    }

    private static CandidateResult Evaluate(StrategyOptions options, IReadOnlyList<DailyBar> daily, IReadOnlyList<FiveMinuteBar> intraday, DateOnly validationStart)
    {
        var result = new BacktestRunner().Run(new BacktestRequest(daily, intraday, 10_000_000m, .2m, options.MaxHoldingDays), new SwingAtrStrategy(options));
        var validationExecutions = result.Executions.Where(execution => DateOnly.FromDateTime(execution.ExecutedAt) >= validationStart).ToArray();
        return new CandidateResult(options, BacktestMetricsCalculator.Calculate(10_000_000m, validationExecutions));
    }

    private sealed record CandidateResult(StrategyOptions Options, BacktestMetrics Metrics);
}
