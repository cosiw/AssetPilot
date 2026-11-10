namespace AssetPilot.Core.Snapshots;

public static class SnapshotSchedule
{
    private static readonly TimeSpan KoreaStandardTimeOffset = TimeSpan.FromHours(9);

    public static bool IsEligible(DateTimeOffset utcNow)
    {
        var koreaTime = utcNow.ToOffset(KoreaStandardTimeOffset);
        return koreaTime.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
            && koreaTime.TimeOfDay >= TimeSpan.FromHours(16);
    }
}
