using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using JobAppManager.Core.Enums;

namespace JobAppManager.App.Converters;

/// <summary>Maps an <see cref="ApplicationStatus"/> to its badge colour. Pass "Dim" as the
/// converter parameter for the badge background rather than the foreground.</summary>
public class StatusBrushConverter : IValueConverter
{
    private static Brush FindBrush(string key) => BrushResources.Find(key);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ApplicationStatus status)
        {
            return DependencyProperty.UnsetValue;
        }

        var suffix = string.Equals(parameter as string, "Dim", StringComparison.Ordinal)
            ? "DimBrush"
            : "Brush";

        // Resolved by name so the palette stays entirely in Colors.xaml.
        return FindBrush($"Status{status}{suffix}");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps an <see cref="InterestLevel"/> to its dot colour.</summary>
public class InterestBrushConverter : IValueConverter
{
    private static Brush FindBrush(string key) => BrushResources.Find(key);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not InterestLevel level)
        {
            return DependencyProperty.UnsetValue;
        }

        return FindBrush($"Interest{level}Brush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class BrushResources
{
    private static readonly Brush Fallback = Brushes.Gray;

    /// <summary>Looks a brush up out of the merged dictionaries, and never throws.
    ///
    /// <c>Application.Current</c> is null outside a running app - in the XAML designer, and in a
    /// test host - and an exception thrown from a converter tears down the whole binding rather
    /// than degrading one colour. A grey fallback is always better than a blank control.</summary>
    internal static Brush Find(string key) =>
        Application.Current?.TryFindResource(key) as Brush
        ?? Application.Current?.TryFindResource("TextMutedBrush") as Brush
        ?? Fallback;
}
