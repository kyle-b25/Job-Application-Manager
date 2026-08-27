using System.Globalization;
using System.Windows;
using JobAppManager.App.Converters;
using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Enums;
using Xunit;

namespace JobAppManager.App.Tests;

/// <summary>The converters are pure functions sitting between the ViewModels and the XAML, and
/// every one of them is reachable only through a binding - so a mistake in one shows up as a
/// blank or wrong-looking control rather than as an exception. They are cheap to test directly.
/// </summary>
public class EnumDisplayNameConverterTests
{
    [Theory]
    [InlineData(ApplicationStatus.Applied, "Applied")]
    [InlineData(ApplicationStatus.Interview, "Interview")]
    [InlineData(ApplicationStatus.Rejected, "Rejected")]
    public void Humanize_NamesEveryStage(ApplicationStatus status, string expected) =>
        Assert.Equal(expected, EnumDisplayNameConverter.Humanize(status));

    // No status is PascalCase any more, but the sort dropdown still renders one, so the split
    // has to keep working somewhere the UI actually depends on it.
    [Theory]
    [InlineData(ApplicationSortField.DateApplied, "Date Applied")]
    [InlineData(ApplicationSortField.CompanyName, "Company Name")]
    [InlineData(ApplicationSortField.Location, "Location")]
    public void Humanize_SplitsPascalCaseIntoWords(ApplicationSortField field, string expected) =>
        Assert.Equal(expected, EnumDisplayNameConverter.Humanize(field));

    [Fact]
    public void Humanize_ReturnsEmpty_ForNull() =>
        Assert.Equal(string.Empty, EnumDisplayNameConverter.Humanize(null));

    [Fact]
    public void Humanize_LeavesRunsOfCapitalsIntact()
    {
        // The rule is "break before a capital that follows a lower-case letter", so an acronym
        // stays whole rather than becoming "A P I Engineer". No status or kind in the app has a
        // run of capitals, so this pins the behaviour rather than endorsing a nicety.
        Assert.Equal("APIEngineer", EnumDisplayNameConverter.Humanize("APIEngineer"));
        Assert.Equal("Job URL", EnumDisplayNameConverter.Humanize("JobURL"));
    }
}

public class TruthyToVisibilityConverterTests
{
    private static readonly TruthyToVisibilityConverter Converter = new();

    private static object Convert(object? value, string? parameter = null) =>
        Converter.Convert(value, typeof(Visibility), parameter, CultureInfo.InvariantCulture);

    [Fact]
    public void Null_IsCollapsed() => Assert.Equal(Visibility.Collapsed, Convert(null));

    [Theory]
    [InlineData(true, "Visible")]
    [InlineData(false, "Collapsed")]
    public void Bool_MapsDirectly(bool value, string expected) =>
        Assert.Equal(Enum.Parse<Visibility>(expected), Convert(value));

    [Theory]
    [InlineData("", "Collapsed")]
    [InlineData("   ", "Collapsed")]
    [InlineData("something", "Visible")]
    public void String_IsVisibleOnlyWhenItHasContent(string value, string expected) =>
        Assert.Equal(Enum.Parse<Visibility>(expected), Convert(value));

    [Theory]
    [InlineData(0, "Collapsed")]
    [InlineData(3, "Visible")]
    public void Int_IsVisibleWhenNonZero(int value, string expected) =>
        Assert.Equal(Enum.Parse<Visibility>(expected), Convert(value));

    [Fact]
    public void EmptyCollection_IsCollapsed()
    {
        // The editor uses this to show "Nothing recorded yet" against a child-row list.
        Assert.Equal(Visibility.Collapsed, Convert(Array.Empty<string>()));
        Assert.Equal(Visibility.Visible, Convert(new[] { "one" }));
    }

    [Fact]
    public void InvertParameter_FlipsTheResult()
    {
        Assert.Equal(Visibility.Collapsed, Convert(true, "Invert"));
        Assert.Equal(Visibility.Visible, Convert(null, "Invert"));
        Assert.Equal(Visibility.Visible, Convert("", "Invert"));
    }

    [Fact]
    public void UnrecognisedParameter_DoesNotInvert()
    {
        // Only the exact string "Invert" flips it - a typo in XAML should fail loudly by showing
        // the wrong thing, not silently invert.
        Assert.Equal(Visibility.Visible, Convert(true, "invert"));
    }
}

public class RateToPercentConverterTests
{
    private static readonly RateToPercentConverter Converter = new();

    private static object Convert(object? value) =>
        Converter.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0d, "0%")]
    [InlineData(1d, "100%")]
    [InlineData(0.5d, "50%")]
    [InlineData(2d / 3d, "67%")]
    public void FormatsAsAWholePercentage(double rate, string expected) =>
        Assert.Equal(expected, Convert(rate));

    [Fact]
    public void NonDouble_FallsBackToZero() => Assert.Equal("0%", Convert(null));
}

public class DateOnlyConverterTests
{
    private static readonly DateOnlyConverter Converter = new();

    [Fact]
    public void RoundTripsThroughDateTime()
    {
        var date = new DateOnly(2026, 5, 20);

        var forward = Converter.Convert(date, typeof(DateTime?), null, CultureInfo.InvariantCulture);
        var back = Converter.ConvertBack(forward, typeof(DateOnly), null, CultureInfo.InvariantCulture);

        Assert.Equal(new DateTime(2026, 5, 20), forward);
        Assert.Equal(date, back);
    }

    [Fact]
    public void NullIsPreservedInBothDirections()
    {
        // DatePicker.SelectedDate is nullable; a cleared picker must not become 01/01/0001.
        Assert.Null(Converter.Convert(null, typeof(DateTime?), null, CultureInfo.InvariantCulture));
        Assert.Null(Converter.ConvertBack(null, typeof(DateOnly), null, CultureInfo.InvariantCulture));
    }
}

public class StatusBrushConverterTests
{
    [Fact]
    public void FallsBackInsteadOfThrowing_WhenThereIsNoApplication()
    {
        // No WPF Application exists in a test host, so every resource lookup misses. The
        // converter has to survive that - a thrown converter takes the whole binding down.
        var exception = Record.Exception(() => new StatusBrushConverter()
            .Convert(ApplicationStatus.Rejected, typeof(object), null, CultureInfo.InvariantCulture));

        Assert.Null(exception);
    }

    [Fact]
    public void ReturnsUnset_ForSomethingThatIsNotAStatus()
    {
        var result = new StatusBrushConverter()
            .Convert("not a status", typeof(object), null, CultureInfo.InvariantCulture);

        Assert.Equal(DependencyProperty.UnsetValue, result);
    }
}
