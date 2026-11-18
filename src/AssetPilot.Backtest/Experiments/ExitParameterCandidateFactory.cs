namespace AssetPilot.Backtest.Experiments;

public sealed class ExitParameterCandidateFactory
{
    private static readonly decimal[] TakeProfitMultipliers = [1m, 1.5m, 2m];
    private static readonly decimal[] StopLossMultipliers = [0.8m, 1m, 1.2m];
    private static readonly int[] MaxHoldingDays = [3, 5, 7];

    public IReadOnlyList<ExitParameterCandidate> Create(ExitParameterExperimentScope scope) =>
        (scope switch
        {
            ExitParameterExperimentScope.TakeProfitOnly =>
                from takeProfit in TakeProfitMultipliers
                select new ExitParameterCandidate(takeProfit, 1m, 5),
            ExitParameterExperimentScope.TakeProfitAndStopLoss =>
                from takeProfit in TakeProfitMultipliers
                from stopLoss in StopLossMultipliers
                select new ExitParameterCandidate(takeProfit, stopLoss, 5),
            ExitParameterExperimentScope.AllExitParameters =>
                from takeProfit in TakeProfitMultipliers
                from stopLoss in StopLossMultipliers
                from maxHoldingDays in MaxHoldingDays
                select new ExitParameterCandidate(takeProfit, stopLoss, maxHoldingDays),
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown experiment scope.")
        })
        .OrderBy(candidate => candidate.TakeProfitAtrMultiplier)
        .ThenBy(candidate => candidate.StopLossAtrMultiplier)
        .ThenBy(candidate => candidate.MaxHoldingDays)
        .ToArray();
}
