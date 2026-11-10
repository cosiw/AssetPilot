using System.Globalization;
using System.Windows.Media;
using AssetPilot.App.Converters;

namespace AssetPilot.App.Tests;

public sealed class MarketValueBrushConverterTests
{
    private static readonly Brush Rise = new SolidColorBrush(Colors.Red);
    private static readonly Brush Fall = new SolidColorBrush(Colors.Blue);
    private static readonly Brush Neutral = new SolidColorBrush(Colors.Black);

    private static MarketValueBrushConverter CreateConverter() => new()
    {
        RiseBrush = Rise,
        FallBrush = Fall,
        NeutralBrush = Neutral
    };

    [Fact]
    public void Convert_returns_rise_brush_for_positive_values()
    {
        var converter = CreateConverter();

        Assert.Same(Rise, converter.Convert(1m, typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Same(Rise, converter.Convert(0.01m, typeof(Brush), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_returns_fall_brush_for_negative_values()
    {
        var converter = CreateConverter();

        Assert.Same(Fall, converter.Convert(-1m, typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Same(Fall, converter.Convert(-0.01m, typeof(Brush), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Convert_returns_neutral_brush_for_zero_or_missing_value()
    {
        var converter = CreateConverter();

        Assert.Same(Neutral, converter.Convert(0m, typeof(Brush), null, CultureInfo.InvariantCulture));
        Assert.Same(Neutral, converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture));
    }
}
