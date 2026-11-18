using AssetPilot.Backtest.Execution;
using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Experiments;

public sealed class BacktestExperimentRunner
{
    private readonly ExitParameterCandidateFactory candidateFactory = new();

    public BacktestExperimentReport Run(BacktestRequest sourceRequest, ExitParameterExperimentScope scope)
    {
        ArgumentNullException.ThrowIfNull(sourceRequest);
        if (sourceRequest.DailyBars.Count == 0) throw new ArgumentException("Daily bars are required.", nameof(sourceRequest));
        if (sourceRequest.FiveMinuteBars.Count == 0) throw new ArgumentException("Five-minute bars are required.", nameof(sourceRequest));

        var results = candidateFactory.Create(scope)
            .Select(candidate => RunCandidate(sourceRequest, candidate))
            .ToArray();

        return new BacktestExperimentReport(Guid.NewGuid(), scope, results);
    }

    private static BacktestExperiment RunCandidate(BacktestRequest sourceRequest, ExitParameterCandidate candidate)
    {
        var options = BaselineStrategyOptions.Create(candidate.ToExitOptions());
        try
        {
            var request = sourceRequest with { MaxHoldingDays = candidate.MaxHoldingDays };
            var result = new BacktestRunner().Run(request, new SwingAtrStrategy(options));
            var metrics = BacktestMetricsCalculator.Calculate(request.StartingCash, result.Executions);
            return new BacktestExperiment(
                candidate,
                options,
                result,
                metrics,
                result.PerformanceReport,
                result.EquityCurve,
                null);
        }
        catch (Exception exception)
        {
            return new BacktestExperiment(candidate, options, null, null, null, null, exception.Message);
        }
    }
}
