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
using Contact = JobAppManager.Core.Entities.Contact;
using JobApplication = JobAppManager.Core.Entities.Application;

namespace JobAppManager.App.ViewModels;

public partial class DashboardViewModel : PageViewModel
{
    private readonly RepositoryFactory _repositories;
    private readonly TimeProvider _timeProvider;

    public DashboardViewModel(
        RepositoryFactory repositories,
        TimeProvider? timeProvider = null)
    {
        _repositories = repositories;
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
    [NotifyPropertyChangedFor(nameof(CanQuickSubmit))]
    [NotifyCanExecuteChangedFor(nameof(QuickSubmitCommand))]
    private string _quickLocation = string.Empty;

    [ObservableProperty]
    private InterestLevel _quickInterest = InterestLevel.Yellow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanQuickSubmit))]
    [NotifyCanExecuteChangedFor(nameof(QuickSubmitCommand))]
    private bool _quickResumeSubmitted;

    [ObservableProperty]
    private bool _quickCoverLetterSubmitted;

    [ObservableProperty]
    private bool _quickFromJobFair;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanQuickSubmit))]
    [NotifyPropertyChangedFor(nameof(QuickContactIncomplete))]
    [NotifyCanExecuteChangedFor(nameof(QuickSubmitCommand))]
    private string _quickContactName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanQuickSubmit))]
    [NotifyPropertyChangedFor(nameof(QuickContactIncomplete))]
    [NotifyCanExecuteChangedFor(nameof(QuickSubmitCommand))]
    private string _quickContactInfo = string.Empty;

    /// <summary>Half a contact. Contact.Email is required by the schema, so a name with no way
    /// to reach the person cannot be stored - and quietly dropping the name the user just typed
    /// would be worse than saying so.</summary>
    public bool QuickContactIncomplete =>
        string.IsNullOrWhiteSpace(QuickContactName) != string.IsNullOrWhiteSpace(QuickContactInfo);

    /// <summary>Company, job title, location and a submitted resume are all required - an
    /// application without them is not one worth tracking. The remaining rule is the contact
    /// pair, which has to be wholly filled in or wholly blank.</summary>
    public bool CanQuickSubmit =>
        !string.IsNullOrWhiteSpace(QuickCompany)
        && !string.IsNullOrWhiteSpace(QuickJobTitle)
        && !string.IsNullOrWhiteSpace(QuickLocation)
        && QuickResumeSubmitted
        && !QuickContactIncomplete
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
            Status = ApplicationStatus.Applied,
            ResumeSubmitted = QuickResumeSubmitted,
            CoverLetterSubmitted = QuickCoverLetterSubmitted,
            FromJobFair = QuickFromJobFair
        };

        // CanQuickSubmit has already ruled out a half-filled pair, so a name here means there is
        // something to reach them by as well. EF inserts the child along with the root.
        if (!string.IsNullOrWhiteSpace(QuickContactName))
        {
            application.Contacts.Add(new Contact
            {
                Name = QuickContactName.Trim(),
                Email = QuickContactInfo.Trim()
            });
        }

        await using (var scope = _repositories.Create())
        {
            await scope.Repository.AddAsync(application);
        }

        QuickCompany = string.Empty;
        QuickJobTitle = string.Empty;
        QuickLocation = string.Empty;
        QuickInterest = InterestLevel.Yellow;
        QuickResumeSubmitted = false;
        QuickCoverLetterSubmitted = false;
        QuickFromJobFair = false;
        QuickContactName = string.Empty;
        QuickContactInfo = string.Empty;

        // Re-read rather than incrementing by hand: the charts and the rates all have to move
        // together, and the repository is the only thing that knows how.
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
                Ry = 5,
                // The axis already names the month and the height already is the count, so a
                // tooltip repeating both only follows the cursor around.
                IsHoverable = false
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
                // A solid pie, and one that holds still: the counts are printed on the slices
                // and the stages are named in the legend, so hover has nothing left to add and
                // a slice sliding out from under the cursor is only distracting.
                InnerRadius = 0,
                HoverPushout = 0,
                IsHoverable = false,
                DataLabelsPaint = new SolidColorPaint(Resource("TextPrimaryBrush")),
                DataLabelsSize = 12,
                DataLabelsPosition = PolarLabelsPosition.Outer,
                DataLabelsFormatter = point => $"{point.Model}"
            })
            .ToArray();
    }

    // ---------------- Chart theming ----------------

    // Bound by the chart controls themselves. Lazy, because Application.Current.Resources is not
    // populated yet while the DI container is being built. Neither chart shows a tooltip any
    // more, so the legend is the only paint left to theme.
    private SolidColorPaint? _legendTextPaint;

    public SolidColorPaint LegendTextPaint =>
        _legendTextPaint ??= new SolidColorPaint(Resource("TextMutedBrush"));


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
