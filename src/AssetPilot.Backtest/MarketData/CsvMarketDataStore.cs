using System.Globalization;
using System.Text;

namespace AssetPilot.Backtest.MarketData;

public sealed class CsvMarketDataStore(string rootDirectory)
{
    public IReadOnlyList<DailyBar> LoadDailyBars(string symbol)
    {
        ValidateSymbol(symbol);
        var path = Path.Combine(rootDirectory, "market-data", symbol, "daily.csv");
        return File.Exists(path) ? new CsvMarketDataLoader().LoadDaily(path) : [];
    }

    public IReadOnlyList<FiveMinuteBar> LoadFiveMinuteBars(string symbol)
    {
        ValidateSymbol(symbol);
        var directory = Path.Combine(rootDirectory, "market-data", symbol, "5m");
        return !Directory.Exists(directory) ? [] : Directory.EnumerateFiles(directory, "*.csv").SelectMany(path => new CsvMarketDataLoader().LoadFiveMinute(path)).OrderBy(bar => bar.Timestamp).ToArray();
    }
    public Task MergeDailyAsync(string symbol, IEnumerable<DailyMarketBar> bars, CancellationToken cancellationToken = default)
    {
        ValidateSymbol(symbol);
        var incoming = bars.ToArray();
        foreach (var bar in incoming) Validate(bar);
        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(rootDirectory, "market-data", symbol, "daily.csv");
        var merged = LoadDaily(path).Concat(incoming).GroupBy(bar => bar.Date).Select(group => group.Last()).OrderBy(bar => bar.Date);
        WriteAtomically(path, DailyHeader, merged.Select(Format));
        return Task.CompletedTask;
    }

    public Task MergeFiveMinuteAsync(string symbol, IEnumerable<IntradayMarketBar> bars, CancellationToken cancellationToken = default)
    {
        ValidateSymbol(symbol);
        foreach (var group in bars.GroupBy(bar => bar.Timestamp.Year))
        {
            var incoming = group.ToArray();
            foreach (var bar in incoming) Validate(bar);
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(rootDirectory, "market-data", symbol, "5m", $"{group.Key}.csv");
            var merged = LoadFiveMinute(path).Concat(incoming).GroupBy(bar => bar.Timestamp).Select(item => item.Last()).OrderBy(bar => bar.Timestamp);
            WriteAtomically(path, FiveMinuteHeader, merged.Select(Format));
        }

        return Task.CompletedTask;
    }

    private static readonly string DailyHeader = "date,open,high,low,close,volume,source,updated_at";
    private static readonly string FiveMinuteHeader = "date,time,open,high,low,close,volume,source,updated_at";

    private static IEnumerable<DailyMarketBar> LoadDaily(string path) =>
        !File.Exists(path) ? [] : File.ReadLines(path).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).Select(ParseDaily);

    private static IEnumerable<IntradayMarketBar> LoadFiveMinute(string path) =>
        !File.Exists(path) ? [] : File.ReadLines(path).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).Select(ParseFiveMinute);

    private static DailyMarketBar ParseDaily(string line)
    {
        var fields = line.Split(',');
        return new DailyMarketBar(DateOnly.ParseExact(fields[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), D(fields[1]), D(fields[2]), D(fields[3]), D(fields[4]), long.Parse(fields[5], CultureInfo.InvariantCulture));
    }

    private static IntradayMarketBar ParseFiveMinute(string line)
    {
        var fields = line.Split(',');
        var timestamp = DateTime.ParseExact($"{fields[0]} {fields[1]}", "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return new IntradayMarketBar(timestamp, D(fields[2]), D(fields[3]), D(fields[4]), D(fields[5]), long.Parse(fields[6], CultureInfo.InvariantCulture));
    }

    private static decimal D(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string Format(DailyMarketBar bar) => string.Join(',', bar.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), N(bar.Open), N(bar.High), N(bar.Low), N(bar.Close), bar.Volume, "kiwoom", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));

    private static string Format(IntradayMarketBar bar) => string.Join(',', bar.Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), bar.Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture), N(bar.Open), N(bar.High), N(bar.Low), N(bar.Close), bar.Volume, "kiwoom", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));

    private static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static void WriteAtomically(string path, string header, IEnumerable<string> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllLines(temporaryPath, new[] { header }.Concat(rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    private static void ValidateSymbol(string symbol)
    {
        if (symbol.Length != 6 || symbol.Any(character => !char.IsAsciiDigit(character))) throw new ArgumentException("Symbol must be a six digit code.", nameof(symbol));
    }

    private static void Validate(DailyMarketBar bar) => Validate(bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);

    private static void Validate(IntradayMarketBar bar)
    {
        if (bar.Timestamp.Second != 0 || bar.Timestamp.Minute % 5 != 0) throw new ArgumentException("Intraday timestamp must be aligned to five minutes.");
        Validate(bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
    }

    private static void Validate(decimal open, decimal high, decimal low, decimal close, long volume)
    {
        if (open <= 0 || high <= 0 || low <= 0 || close <= 0 || volume < 0 || low > Math.Min(open, close) || high < Math.Max(open, close)) throw new ArgumentException("OHLC or volume is invalid.");
    }
}
