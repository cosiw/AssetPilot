using System.Globalization;
using System.Text;

namespace AssetPilot.Backtest.MarketData;

public sealed class CsvMarketDataLoader
{
    public IReadOnlyList<DailyBar> LoadDaily(string path)
    {
        var lines = File.ReadLines(path).ToArray();
        var canonical = IsCanonicalHeader(lines.FirstOrDefault(), "date", "open", "high", "low", "close");
        return lines
        .Skip(1)
        .Where(line => !string.IsNullOrWhiteSpace(line.Trim(',')))
        .Select((line, index) => canonical ? ParseCanonicalDaily(ParseColumns(line), path, index + 2) : ParseDaily(ParseColumns(line), path, index + 2))
        .OrderBy(bar => bar.Date)
        .ToArray();
    }

    public IReadOnlyList<FiveMinuteBar> LoadFiveMinute(string path) => File.ReadLines(path)
        .Skip(1)
        .Where(line => !string.IsNullOrWhiteSpace(line.Trim(',')))
        .Select((line, index) => ParseFiveMinute(ParseColumns(line), path, index + 2))
        .OrderBy(bar => bar.Timestamp)
        .ToArray();

    private static DailyBar ParseDaily(IReadOnlyList<string> fields, string path, int lineNumber)
    {
        RequireColumnCount(fields, 8, path, lineNumber);
        return new DailyBar(
            ParseDate(fields[0], path, lineNumber),
            ParseDecimal(fields[5], path, lineNumber),
            ParseDecimal(fields[6], path, lineNumber),
            ParseDecimal(fields[7], path, lineNumber),
            ParseDecimal(fields[1], path, lineNumber));
    }

    private static DailyBar ParseCanonicalDaily(IReadOnlyList<string> fields, string path, int lineNumber)
    {
        RequireColumnCount(fields, 5, path, lineNumber);
        return new DailyBar(ParseDate(fields[0], path, lineNumber), ParseDecimal(fields[1], path, lineNumber), ParseDecimal(fields[2], path, lineNumber), ParseDecimal(fields[3], path, lineNumber), ParseDecimal(fields[4], path, lineNumber));
    }

    private static FiveMinuteBar ParseFiveMinute(IReadOnlyList<string> fields, string path, int lineNumber)
    {
        RequireColumnCount(fields, 6, path, lineNumber);
        var date = ParseDate(fields[0], path, lineNumber);
        if (!TimeOnly.TryParse(fields[1], CultureInfo.InvariantCulture, out var time))
        {
            throw Invalid(path, lineNumber, "시간");
        }

        return new FiveMinuteBar(
            date.ToDateTime(time),
            ParseDecimal(fields[2], path, lineNumber),
            ParseDecimal(fields[3], path, lineNumber),
            ParseDecimal(fields[4], path, lineNumber),
            ParseDecimal(fields[5], path, lineNumber));
    }

    private static DateOnly ParseDate(string value, string path, int lineNumber) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date)
            ? date
            : throw Invalid(path, lineNumber, "일자");

    private static decimal ParseDecimal(string value, string path, int lineNumber) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Invalid(path, lineNumber, "숫자");

    private static void RequireColumnCount(IReadOnlyList<string> fields, int count, string path, int lineNumber)
    {
        if (fields.Count < count)
        {
            throw Invalid(path, lineNumber, "열 개수");
        }
    }

    private static FormatException Invalid(string path, int lineNumber, string value) =>
        new($"{path} {lineNumber}행의 {value} 형식이 올바르지 않습니다.");

    private static IReadOnlyList<string> ParseColumns(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append(character);
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static bool IsCanonicalHeader(string? line, params string[] expected)
    {
        if (line is null) return false;
        var fields = ParseColumns(line);
        return fields.Count >= expected.Length && expected.Select((value, index) => string.Equals(fields[index], value, StringComparison.OrdinalIgnoreCase)).All(equal => equal);
    }
}
