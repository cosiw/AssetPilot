using System.Collections.ObjectModel;
using AssetPilot.Core.Snapshots;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace AssetPilot.App.ViewModels;

public partial class AssetHistoryViewModel(IAccountSnapshotReader snapshotReader) : ObservableObject
{
    private Guid? _accountId;
    private IReadOnlyList<AccountSnapshotSummary> _allSummaries = [];

    public ObservableCollection<DateOnly> AvailableSnapshotDates { get; } = [];

    [ObservableProperty]
    public partial AssetHistoryRange SelectedRange { get; set; } = AssetHistoryRange.Week;

    [ObservableProperty]
    public partial DateOnly? SelectedSnapshotDate { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistory))]
    [NotifyPropertyChangedFor(nameof(HasCashBalanceForSelectedDate))]
    [NotifyPropertyChangedFor(nameof(HasCashBalanceGap))]
    [NotifyPropertyChangedFor(nameof(SelectedSummary))]
    [NotifyPropertyChangedFor(nameof(HasComposition))]
    [NotifyPropertyChangedFor(nameof(CompositionEmptyMessage))]
    public partial IReadOnlyList<AccountSnapshotSummary> Summaries { get; set; } = [];

    public bool HasHistory => Summaries.Count > 0;

    public bool HasCashBalanceForSelectedDate =>
        SelectedSnapshotDate is { } date &&
        Summaries.FirstOrDefault(summary => summary.SnapshotDate == date)?.HasCashBalance == true;

    public bool HasCashBalanceGap => Summaries.Any(summary => !summary.HasCashBalance);

    public bool IsWeekSelected => SelectedRange == AssetHistoryRange.Week;

    public bool IsMonthSelected => SelectedRange == AssetHistoryRange.Month;

    public bool IsThreeMonthsSelected => SelectedRange == AssetHistoryRange.ThreeMonths;

    public bool IsAllSelected => SelectedRange == AssetHistoryRange.All;

    public bool HasComposition =>
        SelectedSummary is { HasCashBalance: true } summary &&
        summary.TotalEvaluationAmount + summary.CashBalance > 0;

    public AccountSnapshotSummary? SelectedSummary =>
        SelectedSnapshotDate is { } date
            ? Summaries.FirstOrDefault(summary => summary.SnapshotDate == date)
            : null;

    public ISeries[] AssetSeries { get; private set; } = [];

    public Axis[] AssetHistoryXAxes { get; } =
    [
        new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("M/d"))
        {
            LabelsRotation = 0,
            TextSize = 12
        }
    ];

    public Axis[] AssetHistoryYAxes { get; } =
    [
        new Axis
        {
            Labeler = value => $"{value / 10_000d:N0}만",
            TextSize = 12
        }
    ];

    public ISeries[] CompositionSeries { get; private set; } = [];

    public string EmptyMessage => HasHistory
        ? string.Empty
        : "저장된 자산 기록이 없습니다. 다음 예약 저장 후 표시됩니다.";

    public string CompositionEmptyMessage => SelectedSummary is null
        ? "기준일을 선택하면 자산 구성을 표시합니다."
        : !SelectedSummary.HasCashBalance
            ? "예수금 저장 전 데이터입니다."
            : "국내주식과 예수금의 합계가 0원이라 자산 구성을 표시할 수 없습니다.";

    public async Task LoadAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        _accountId = accountId;
        _allSummaries = await snapshotReader.GetSummariesAsync(accountId, null, null, cancellationToken);
        ApplySelectedRange();
    }

    public void Clear()
    {
        _accountId = null;
        _allSummaries = [];
        Summaries = [];
        AvailableSnapshotDates.Clear();
        SelectedSnapshotDate = null;
        RebuildCharts();
    }

    partial void OnSelectedRangeChanged(AssetHistoryRange value)
    {
        OnPropertyChanged(nameof(IsWeekSelected));
        OnPropertyChanged(nameof(IsMonthSelected));
        OnPropertyChanged(nameof(IsThreeMonthsSelected));
        OnPropertyChanged(nameof(IsAllSelected));

        if (_accountId is not null)
        {
            ApplySelectedRange();
        }
    }

    partial void OnSelectedSnapshotDateChanged(DateOnly? value)
    {
        OnPropertyChanged(nameof(HasCashBalanceForSelectedDate));
        OnPropertyChanged(nameof(SelectedSummary));
        OnPropertyChanged(nameof(HasComposition));
        OnPropertyChanged(nameof(CompositionEmptyMessage));
        RebuildCompositionSeries();
    }

    [RelayCommand]
    private void SelectRange(AssetHistoryRange range) => SelectedRange = range;

    private void ApplySelectedRange()
    {
        var latestDate = _allSummaries.LastOrDefault()?.SnapshotDate;
        if (latestDate is null)
        {
            Summaries = [];
            AvailableSnapshotDates.Clear();
            SelectedSnapshotDate = null;
            OnPropertyChanged(nameof(EmptyMessage));
            RebuildCharts();
            return;
        }

        DateOnly? from = SelectedRange switch
        {
            AssetHistoryRange.Week => latestDate.Value.AddDays(-6),
            AssetHistoryRange.Month => latestDate.Value.AddMonths(-1),
            AssetHistoryRange.ThreeMonths => latestDate.Value.AddMonths(-3),
            AssetHistoryRange.All => null,
            _ => throw new ArgumentOutOfRangeException()
        };
        Summaries = from is { } startDate
            ? _allSummaries.Where(summary => summary.SnapshotDate >= startDate).ToArray()
            : _allSummaries;

        AvailableSnapshotDates.Clear();
        foreach (var summary in Summaries)
        {
            AvailableSnapshotDates.Add(summary.SnapshotDate);
        }

        if (SelectedSnapshotDate is not { } selectedDate || !AvailableSnapshotDates.Contains(selectedDate))
        {
            SelectedSnapshotDate = latestDate;
        }

        OnPropertyChanged(nameof(EmptyMessage));
        RebuildCharts();
    }

    private void RebuildCharts()
    {
        AssetSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "총자산",
                Values = Summaries
                    .Select(summary => new DateTimePoint(
                        summary.SnapshotDate.ToDateTime(TimeOnly.MinValue),
                        (double)summary.TotalAssetAmount))
                    .ToArray(),
                Stroke = new SolidColorPaint(new SKColor(18, 52, 82), 3),
                Fill = null,
                GeometrySize = 7,
                LineSmoothness = 0,
                YToolTipLabelFormatter = point => FormatWon(point.Coordinate.PrimaryValue)
            },
            new LineSeries<DateTimePoint>
            {
                Name = "예수금",
                Values = Summaries
                    .Select(summary => new DateTimePoint(
                        summary.SnapshotDate.ToDateTime(TimeOnly.MinValue),
                        summary.HasCashBalance ? (double?)summary.CashBalance : null))
                    .ToArray(),
                Stroke = new SolidColorPaint(new SKColor(30, 98, 183), 3),
                Fill = null,
                GeometrySize = 7,
                LineSmoothness = 0,
                EnableNullSplitting = true,
                YToolTipLabelFormatter = point => FormatWon(point.Coordinate.PrimaryValue)
            }
        ];
        OnPropertyChanged(nameof(AssetSeries));
        RebuildCompositionSeries();
    }

    private void RebuildCompositionSeries()
    {
        CompositionSeries = HasComposition && SelectedSummary is { } summary
            ?
            [
                new PieSeries<decimal>
                {
                    Name = "국내주식",
                    Values = [summary.TotalEvaluationAmount],
                    Fill = new SolidColorPaint(new SKColor(18, 52, 82)),
                    InnerRadius = 55,
                    DataLabelsSize = 13,
                    DataLabelsFormatter = point => FormatWon(point.Coordinate.PrimaryValue),
                    ToolTipLabelFormatter = point => $"국내주식: {FormatWon(point.Coordinate.PrimaryValue)}"
                },
                new PieSeries<decimal>
                {
                    Name = "예수금",
                    Values = [summary.CashBalance],
                    Fill = new SolidColorPaint(new SKColor(75, 115, 158)),
                    InnerRadius = 55,
                    DataLabelsSize = 13,
                    DataLabelsFormatter = point => FormatWon(point.Coordinate.PrimaryValue),
                    ToolTipLabelFormatter = point => $"예수금: {FormatWon(point.Coordinate.PrimaryValue)}"
                }
            ]
            : [];
        OnPropertyChanged(nameof(CompositionSeries));
    }

    private static string FormatWon(double amount) => $"{amount:N0}원";
}
