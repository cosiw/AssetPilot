using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AssetPilot.App.Tests;

public sealed class PortfolioDataGridLayoutTests
{
    [Fact]
    public void Main_window_displays_snapshot_summary_and_asset_history_charts()
    {
        var xaml = File.ReadAllText(FindMainWindowXaml());

        Assert.Contains("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("Portfolio.TotalAssetAmount", xaml, StringComparison.Ordinal);
        Assert.Contains("Portfolio.CashBalance", xaml, StringComparison.Ordinal);
        Assert.Contains("SnapshotCapturedAtKorea", xaml, StringComparison.Ordinal);
        Assert.Contains("AssetHistory.SelectRangeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("<lvc:CartesianChart", xaml, StringComparison.Ordinal);
        Assert.Contains("<lvc:PieChart", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Portfolio_cell_styles_apply_left_padding_to_stock_and_right_padding_to_numeric_columns()
    {
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                var dictionary = (ResourceDictionary)Application.LoadComponent(
                    new Uri("/AssetPilot.App;component/Themes/Controls/DataGrid.xaml", UriKind.Relative));

                Assert.Equal(new Thickness(0), GetPadding(dictionary, "DataGridCell.FirstColumn"));
                Assert.Equal(new Thickness(0, 0, 17, 0), GetPadding(dictionary, "DataGridCell.Numeric"));
                Assert.Equal(new Thickness(0, 0, 6, 0), GetMargin(dictionary, "Text.DataGridNumeric"));
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(exception);
    }

    private static Thickness GetPadding(ResourceDictionary dictionary, string key)
    {
        var style = Assert.IsType<Style>(dictionary[key]);
        var setter = Assert.Single(style.Setters.Cast<Setter>(), candidate => candidate.Property == Control.PaddingProperty);

        return Assert.IsType<Thickness>(setter.Value);
    }

    private static Thickness GetMargin(ResourceDictionary dictionary, string key)
    {
        var style = Assert.IsType<Style>(dictionary[key]);
        var setter = Assert.Single(style.Setters.Cast<Setter>(), candidate => candidate.Property == FrameworkElement.MarginProperty);

        return Assert.IsType<Thickness>(setter.Value);
    }

    private static string FindMainWindowXaml()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AssetPilot.App", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("MainWindow.xaml을 찾을 수 없습니다.");
    }
}
