using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AssetPilot.App.Converters;

public sealed class MarketValueBrushConverter : IValueConverter
{
    public Brush? RiseBrush { get; set; }

    public Brush? FallBrush { get; set; }

    public Brush? NeutralBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal number)
        {
            return NeutralBrush;
        }

        return number > 0m ? RiseBrush : number < 0m ? FallBrush : NeutralBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
