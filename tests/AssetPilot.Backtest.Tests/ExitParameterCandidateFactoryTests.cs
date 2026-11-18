using AssetPilot.Backtest.Experiments;

namespace AssetPilot.Backtest.Tests;

public sealed class ExitParameterCandidateFactoryTests
{
    [Theory]
    [InlineData(ExitParameterExperimentScope.TakeProfitOnly, 3)]
    [InlineData(ExitParameterExperimentScope.TakeProfitAndStopLoss, 9)]
    [InlineData(ExitParameterExperimentScope.AllExitParameters, 27)]
    public void Create_returns_the_expected_number_of_unique_candidates(
        ExitParameterExperimentScope scope,
        int expectedCount)
    {
        var candidates = new ExitParameterCandidateFactory().Create(scope);

        Assert.Equal(expectedCount, candidates.Count);
        Assert.Equal(expectedCount, candidates.Distinct().Count());
        Assert.All(candidates, candidate =>
        {
            Assert.True(candidate.TakeProfitAtrMultiplier > 0m);
            Assert.True(candidate.StopLossAtrMultiplier > 0m);
            Assert.True(candidate.MaxHoldingDays >= 1);
        });
    }
}
