using System.Globalization;
using System.Windows;
using System.Windows.Data;
using JobAppManager.Core.Enums;

namespace JobAppManager.App.Converters;

/// <summary>True (or non-empty, or non-null) becomes Visible. Pass "Invert" to flip it.
/// One converter rather than the usual four, because the rule is the same in every case:
/// "is there something here?".</summary>
public class TruthyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            double d => d != 0d,
            System.Collections.ICollection c => c.Count > 0,
            System.Collections.IEnumerable e => e.GetEnumerator().MoveNext(),
            _ => true
        };

        if (string.Equals(parameter as string, "Invert", StringComparison.Ordinal))
        {
            truthy = !truthy;
        }

        return truthy ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Formats a 0..1 rate as a whole-number percentage.</summary>
public class RateToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double rate ? $"{rate * 100:0}%" : "0%";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Splits a PascalCase enum name into words, so PhoneScreen shows as "Phone Screen".
/// Keeps the enum members idiomatic C# without leaking that into the UI.</summary>
public class EnumDisplayNameConverter : IValueConverter
{
    public static string Humanize(object? value)
    {
        // InterestLevel is named for the colour of its dot, which is right in the code and
        // useless as a label - "Red" does not tell anyone whether that is good or bad.
        if (value is InterestLevel interest)
        {
            return interest switch
            {
                InterestLevel.Red => "Low interest",
                InterestLevel.Yellow => "Interested",
                InterestLevel.Green => "High interest",
                _ => interest.ToString()
            };
        }

        var name = value?.ToString();

        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(name[i]);
        }

        return builder.ToString();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Humanize(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>DateOnly to DateTime? and back, because DatePicker.SelectedDate is DateTime?.</summary>
public class DateOnlyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : null;
}
