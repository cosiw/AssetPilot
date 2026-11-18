namespace AssetPilot.MarketDataWorker;

public static class MarketDataSchedule
{
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);
    public static bool IsEligible(DateTimeOffset now)
    {
        var korea = now.ToOffset(KoreaOffset);
        return korea.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && korea.TimeOfDay >= new TimeSpan(16, 5, 0);
    }
}
