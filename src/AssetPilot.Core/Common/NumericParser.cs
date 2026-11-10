using System.Globalization;

namespace AssetPilot.Core.Common;

public static class NumericParser
{
    public static decimal ToDecimal(string? value) =>
        decimal.Parse(value ?? "0", NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    public static int ToInt32(string? value) =>
        int.Parse(value ?? "0", NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    public static long ToInt64(string? value) =>
        long.Parse(value ?? "0", NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
}
