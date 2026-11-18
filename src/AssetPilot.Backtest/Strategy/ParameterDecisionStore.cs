using System.Text.Json;

namespace AssetPilot.Backtest.Strategy;

public sealed class ParameterDecisionStore(string rootDirectory)
{
    public async Task<ParameterRecommendation?> LoadLatestPendingAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(rootDirectory, "strategy-state", "parameter-decisions.jsonl");
        if (!File.Exists(path)) return null;
        var line = (await File.ReadAllLinesAsync(path, cancellationToken)).LastOrDefault();
        if (string.IsNullOrWhiteSpace(line)) return null;
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!Enum.TryParse<ParameterDecisionStatus>(root.GetProperty("Status").GetString(), out var status) || status != ParameterDecisionStatus.PendingApproval) return null;
        return root.Deserialize<ParameterRecommendation>();
    }
    public async Task SavePendingAsync(ParameterRecommendation recommendation, CancellationToken cancellationToken = default)
    {
        if (recommendation.Status != ParameterDecisionStatus.PendingApproval || recommendation.Options is null) throw new ArgumentException("Only a pending recommendation with options can be saved.", nameof(recommendation));
        var directory = Path.Combine(rootDirectory, "strategy-state");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "parameter-decisions.jsonl");
        var record = JsonSerializer.Serialize(recommendation);
        await File.AppendAllTextAsync(path, record + Environment.NewLine, cancellationToken);
    }

    public async Task ApproveAsync(ParameterRecommendation recommendation, string approvedBy, CancellationToken cancellationToken = default)
    {
        if (recommendation.Status != ParameterDecisionStatus.PendingApproval || recommendation.Options is null) throw new ArgumentException("Only a pending recommendation with options can be approved.", nameof(recommendation));
        var directory = Path.Combine(rootDirectory, "strategy-state");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "active-parameters.json");
        var temporary = path + ".tmp";
        var content = JsonSerializer.Serialize(new { recommendation.EffectiveFrom, ApprovedBy = approvedBy, ApprovedAt = DateTimeOffset.UtcNow, Options = recommendation.Options }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task RejectAsync(ParameterRecommendation recommendation, string rejectedBy, CancellationToken cancellationToken = default)
    {
        if (recommendation.Status != ParameterDecisionStatus.PendingApproval) throw new ArgumentException("Only a pending recommendation can be rejected.", nameof(recommendation));
        var directory = Path.Combine(rootDirectory, "strategy-state");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "parameter-decisions.jsonl");
        var record = JsonSerializer.Serialize(new { Status = ParameterDecisionStatus.Rejected, recommendation.DecisionDate, recommendation.EffectiveFrom, recommendation.Options, recommendation.Reason, RejectedBy = rejectedBy, RejectedAt = DateTimeOffset.UtcNow });
        await File.AppendAllTextAsync(path, record + Environment.NewLine, cancellationToken);
    }
}
