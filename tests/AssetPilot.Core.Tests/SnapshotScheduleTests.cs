using AssetPilot.Core.Snapshots;

namespace AssetPilot.Core.Tests;

public sealed class SnapshotScheduleTests
{
    [Theory]
    [InlineData("2026-11-09T06:59:00+00:00", false)]
    [InlineData("2026-11-09T07:00:00+00:00", true)]
    [InlineData("2026-11-09T14:59:00+00:00", true)]
    [InlineData("2026-11-09T15:00:00+00:00", false)]
    [InlineData("2026-11-14T08:00:00+00:00", false)]
    public void IsEligible_returns_expected_value(string utc, bool expected)
    {
        Assert.Equal(expected, SnapshotSchedule.IsEligible(DateTimeOffset.Parse(utc)));
    }
}
