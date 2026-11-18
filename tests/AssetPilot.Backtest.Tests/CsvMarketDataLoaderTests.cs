using AssetPilot.Backtest.MarketData;

namespace AssetPilot.Backtest.Tests;

public sealed class CsvMarketDataLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public void LoadDaily_reads_open_high_low_from_f_g_h_and_sorts_ascending()
    {
        Directory.CreateDirectory(_directory);
        var path = WriteFile("daily.csv", "일자,종가,대비,등락률,NAV,시가,고가,저가\n2026/01/02,110,0,0,0,101,115,99\n2026/01/01,100,0,0,0,91,105,89");

        var bars = new CsvMarketDataLoader().LoadDaily(path);

        Assert.Collection(bars,
            bar => Assert.Equal(new DailyBar(new DateOnly(2026, 1, 1), 91m, 105m, 89m, 100m), bar),
            bar => Assert.Equal(new DailyBar(new DateOnly(2026, 1, 2), 101m, 115m, 99m, 110m), bar));
    }

    [Fact]
    public void LoadFiveMinute_sorts_date_and_time_ascending()
    {
        Directory.CreateDirectory(_directory);
        var path = WriteFile("intraday.csv", "일자,시간,시가,고가,저가,종가\n2026/01/02,09:05:00,110,115,109,114\n2026/01/02,09:00:00,100,111,99,110");

        var bars = new CsvMarketDataLoader().LoadFiveMinute(path);

        Assert.Equal(new DateTime(2026, 1, 2, 9, 0, 0), bars[0].Timestamp);
        Assert.Equal(100m, bars[0].Open);
    }

    [Fact]
    public void LoadDaily_reads_canonical_header_format()
    {
        Directory.CreateDirectory(_directory);
        var path = WriteFile("canonical-daily.csv", "date,open,high,low,close,volume,source,updated_at\n2026-01-02,101,115,99,110,10,kiwoom,2026-01-02T16:05:00+09:00");

        var bars = new CsvMarketDataLoader().LoadDaily(path);

        Assert.Equal(new DailyBar(new DateOnly(2026, 1, 2), 101m, 115m, 99m, 110m), Assert.Single(bars));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteFile(string name, string contents)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
