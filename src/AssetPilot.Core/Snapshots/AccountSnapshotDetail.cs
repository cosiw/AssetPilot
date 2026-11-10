namespace AssetPilot.Core.Snapshots;

public sealed record AccountSnapshotDetail(
    AccountSnapshotSummary Summary,
    IReadOnlyList<PositionSnapshot> Positions);
