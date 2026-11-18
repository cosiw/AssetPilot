using AssetPilot.Backtest.Strategy;

namespace AssetPilot.Backtest.Tests;

public sealed class ParameterDecisionStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SavePendingAsync_does_not_create_active_parameters()
    {
        var store = new ParameterDecisionStore(_root);
        var recommendation = new ParameterRecommendation(ParameterDecisionStatus.PendingApproval, new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 1), new StrategyOptions { SmaPeriod = 40 }, "test");

        await store.SavePendingAsync(recommendation);

        Assert.False(File.Exists(Path.Combine(_root, "strategy-state", "active-parameters.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
