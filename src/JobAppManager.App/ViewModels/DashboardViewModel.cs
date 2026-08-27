using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobAppManager.App.Converters;
using JobAppManager.App.Services;
using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Enums;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

// System.Windows.Application is already in scope here for the resource lookups the charts do,
// so the entity gets an alias rather than an ambiguous import.
using JobApplication = JobAppManager.Core.Entities.Application;

namespace JobAppManager.App.ViewModels;

public partial class DashboardViewModel : PageViewModel
{
    private readonly RepositoryFactory _repositories;
    private readonly INavigationService _navigation;
    private readonly TimeProvider _timeProvider;

    public DashboardViewModel(
        RepositoryFactory repositories,
        INavigationService navigation,
        TimeProvider? timeProvider = null)
    {
        _repositories = repositories;
        _navigation = navigation;
        _timeProvider = timeProvider ?? TimeProvider.System;

        InterestLevels = Enum.GetValues<InterestLevel>();
    }

    public override string Title => "Dashboard";

    public override string? Subtitle => TotalApplications == 0
        ? null
        : $"{ActiveApplications} still in play out of {TotalApplications} tracked.";

    /// <summary>Today on the injected clock - the date a quick-submitted application gets.</summary>
    private DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    // ---------------- Quick submit ----------------

    public IReadOnlyList<InterestLevel> InterestLevels { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanQuickSubmit))]
    [NotifyCanExecuteChangedFor(nameof(QuickSubmitCommand))]
    private string _quickCompany = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanQuickSubmit))]
    [NotifyCanExecuteChangedFor(nameof(QuickSubmitCommand))]
    private string _quickJobTitle = string.Empty;

    [ObservableProperty]
    private string _quickLocation = string.Empty;

    [ObservableProperty]
    private InterestLevel _quickInterest = InterestLevel.Yellow;

    /// <summary>Company and title are the two fields the entity actually requires; everything
    /// else on the form has a sensible default, which is the point of a quick submit.</summary>
    public bool CanQuickSubmit =>
        !string.IsNullOrWhiteSpace(QuickCompany)
        && !string.IsNullOrWhiteSpace(QuickJobTitle)
        && !IsBusy;

    /// <summary>Adds an application dated today, straight from the dashboard. It goes in as
    /// Applied through <see cref="IApplicationRepository.AddAsync"/>, which seeds the opening
    /// status-history entry - so the new row counts in every statistic on this page immediately.</summary>
    [RelayCommand(CanExecute = nameof(CanQuickSubmit))]
    private async Task QuickSubmitAsync()
    {
        var application = new JobApplication
        {
            CompanyName = QuickCompany.Trim(),
            JobTitle = QuickJobTitle.Trim(),
            Location = QuickLocation?.Trim() ?? string.Empty,
            InterestLevel = QuickInterest,
            DateApplied = Today,
            Status = ApplicationStatus.Applied
        };

        await using (var scope = _repositories.Create())
        {
            await scope.Repository.AddAsync(application);
        }

        QuickCompany = string.Empty;
        QuickJobTitle = string.Empty;
        QuickLocation = string.Empty;
        QuickInterest = InterestLevel.Yellow;

        // Re-read rather than incrementing by hand: the charts, the rates, and the stage
        // breakdown all have to move together, and the repository is the only thing that knows how.
        await ActivateAsync();
    }

    [ObservableProperty]
    private int _totalApplications;

    [ObservableProperty]
    private int _activeApplications;

    [ObservableProperty]
    private double _interviewRate;

    [ObservableProperty]
    private double _rejectionRate;

    [ObservableProperty]
    private int _appliedLast7Days;

    [ObservableProperty]
    private int _appliedLast30Days;

    [ObservableProperty]
    private ISeries[] _overTimeSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] _overTimeXAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _overTimeYAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private ISeries[] _statusSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private ISeries[] _stageDurationSeries = Array.Empty<ISeries>();

    [ObservableProperty]
    private Axis[] _stageDurationXAxes = Array.Empty<Axis>();

    [ObservableProperty]
    private Axis[] _stageDurationYAxes = Array.Empty<Axis>();

    /// <summary>True until at least one status transition exists anywhere. Without one there is
    /// no elapsed time to average, and an empty bar chart says less than a sentence does.</summary>
    [ObservableProperty]
    private bool _hasStageDurations;

    public bool IsEmpty => TotalApplications == 0 && !IsBusy;

    public bool HasData => TotalApplications > 0;

    public override async Task ActivateAsync()
    {
        IsBusy = true;

        try
        {
            JobHuntStatistics stats;

            await using (var scope = _repositories.Create())
            {
                stats = await scope.Repository.GetStatisticsAsync();
            }

            TotalApplications = stats.TotalApplications;
            ActiveApplications = stats.ActiveApplications;
            InterviewRate = stats.InterviewRate;
            RejectionRate = stats.RejectionRate;
            AppliedLast7Days = stats.AppliedLast7Days;
            AppliedLast30Days = stats.AppliedLast30Days;

            BuildOverTimeChart(stats);
            BuildStatusChart(stats);
            BuildStageDurationChart(stats);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(CanQuickSubmit));
            QuickSubmitCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>The empty state offers the full editor for anyone who wants more than the four
    /// fields the quick-submit box above it asks for.</summary>
    [RelayCommand]
    private void AddFirst() => _navigation.GoToNewApplication();

    // ---------------- Charts ----------------

    private void BuildOverTimeChart(JobHuntStatistics stats)
    {
        // The repository returns only months that had applications. Left as-is the chart would
        // draw a quiet January and a busy March as neighbours and imply they were consecutive,
        // so the empty months are filled back in here.
        var filled = FillMonthGaps(stats.MonthlyCounts).ToList();

        OverTimeSeries = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Name = "Applications",
                Values = filled.Select(m => m.Count).ToArray(),
                Fill = new SolidColorPaint(Accent()),
                Stroke = null,
                MaxBarWidth = 44,
                Rx = 5,
                Ry = 5
            }
        };

        OverTimeXAxes = new[]
        {
            NewAxis(filled
                .Select(m => new DateTime(m.Year, m.Month, 1).ToString("MMM yy", CultureInfo.CurrentCulture))
                .ToArray())
        };

        OverTimeYAxes = new[] { NewValueAxis() };
    }

    /// <summary>Inserts zero-count months between the ones that exist, so the x axis is a real
    /// timeline rather than a list of the months that happened to be busy.</summary>
    private static IEnumerable<MonthlyApplicationCount> FillMonthGaps(
        IReadOnlyList<MonthlyApplicationCount> counts)
    {
        if (counts.Count == 0)
        {
            yield break;
        }

        var cursor = new DateTime(counts[0].Year, counts[0].Month, 1);
        var last = new DateTime(counts[^1].Year, counts[^1].Month, 1);

        // A long-running hunt could span years; cap the axis at the most recent 24 months so the
        // columns stay wide enough to read.
        if ((last.Year - cursor.Year) * 12 + last.Month - cursor.Month > 23)
        {
            cursor = last.AddMonths(-23);
        }

        while (cursor <= last)
        {
            var match = counts.FirstOrDefault(c => c.Year == cursor.Year && c.Month == cursor.Month);
            yield return match ?? new MonthlyApplicationCount(cursor.Year, cursor.Month, 0);
            cursor = cursor.AddMonths(1);
        }
    }

    private void BuildStatusChart(JobHuntStatistics stats)
    {
        // Empty stages are dropped: a pie slice of zero is invisible but still takes a legend
        // entry, which just makes the legend harder to read.
        StatusSeries = stats.CountByStatus
            .Where(kvp => kvp.Value > 0)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (ISeries)new PieSeries<int>
            {
                Name = EnumDisplayNameConverter.Humanize(kvp.Key),
                Values = new[] { kvp.Value },
                Fill = new SolidColorPaint(StatusColor(kvp.Key)),
                Stroke = null,
                InnerRadius = 58,
                DataLabelsPaint = new SolidColorPaint(Resource("TextPrimaryBrush")),
                DataLabelsSize = 12,
                DataLabelsPosition = PolarLabelsPosition.Outer,
                DataLabelsFormatter = point => $"{point.Model}",
                ToolTipLabelFormatter = point =>
                    $"{EnumDisplayNameConverter.Humanize(kvp.Key)}: {point.Model}"
            })
            .ToArray();
    }

    private void BuildStageDurationChart(JobHuntStatistics stats)
    {
        var stages = stats.AverageDaysInStage.OrderBy(s => s.Stage).ToList();

        HasStageDurations = stages.Count > 0;

        if (!HasStageDurations)
        {
            StageDurationSeries = Array.Empty<ISeries>();
            StageDurationXAxes = Array.Empty<Axis>();
            StageDurationYAxes = Array.Empty<Axis>();
            return;
        }

        StageDurationSeries = new ISeries[]
        {
            new RowSeries<double>
            {
                Name = "Average days",
                Values = stages.Select(s => Math.Round(s.AverageDays, 1)).ToArray(),
                Fill = new SolidColorPaint(Accent()),
                Stroke = null,
                MaxBarWidth = 26,
                Rx = 5,
                Ry = 5,
                DataLabelsPaint = new SolidColorPaint(Resource("TextPrimaryBrush")),
                DataLabelsSize = 11,
                DataLabelsPosition = DataLabelsPosition.End,
                DataLabelsFormatter = point => $"{point.Model:0.#} d"
            }
        };

        // Rows are horizontal, so the categories live on the Y axis and the values on the X.
        StageDurationYAxes = new[]
        {
            NewAxis(stages.Select(s => EnumDisplayNameConverter.Humanize(s.Stage)).ToArray())
        };

        StageDurationXAxes = new[] { NewValueAxis() };
    }

    // ---------------- Chart theming ----------------

    // Bound by the chart controls themselves. Lazy, because Application.Current.Resources is not
    // populated yet while the DI container is being built.
    private SolidColorPaint? _legendTextPaint;
    private SolidColorPaint? _tooltipTextPaint;
    private SolidColorPaint? _tooltipBackgroundPaint;

    public SolidColorPaint LegendTextPaint =>
        _legendTextPaint ??= new SolidColorPaint(Resource("TextMutedBrush"));

    public SolidColorPaint TooltipTextPaint =>
        _tooltipTextPaint ??= new SolidColorPaint(Resource("TextPrimaryBrush"));

    public SolidColorPaint TooltipBackgroundPaint =>
        _tooltipBackgroundPaint ??= new SolidColorPaint(Resource("SurfaceRaisedBrush"));


    // LiveCharts defaults are drawn for a white page: black labels, black separators, a white
    // tooltip. Every axis and paint has to be set explicitly or the charts look broken here.

    private static Axis NewAxis(string[] labels) => new()
    {
        Labels = labels,
        LabelsPaint = new SolidColorPaint(Resource("TextMutedBrush")),
        TextSize = 11.5,
        SeparatorsPaint = null,
        TicksPaint = null,
        ForceStepToMin = true,
        MinStep = 1
    };

    private static Axis NewValueAxis() => new()
    {
        LabelsPaint = new SolidColorPaint(Resource("TextMutedBrush")),
        TextSize = 11.5,
        MinLimit = 0,
        SeparatorsPaint = new SolidColorPaint(Resource("ChartGridBrush")) { StrokeThickness = 1 },
        TicksPaint = null
    };

    private static SKColor Accent() => Resource("AccentBrush");

    private static SKColor StatusColor(ApplicationStatus status) => Resource($"Status{status}Brush");

    /// <summary>Reads a brush out of Colors.xaml as a Skia colour, so the charts and the badges
    /// are literally the same palette and cannot drift apart.</summary>
    private static SKColor Resource(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }

        return SKColors.Gray;
    }
}
