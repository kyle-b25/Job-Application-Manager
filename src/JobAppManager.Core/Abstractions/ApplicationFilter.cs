using JobAppManager.Core.Enums;

namespace JobAppManager.Core.Abstractions;

/// <summary>Columns the spreadsheet page can sort by.</summary>
public enum ApplicationSortField
{
    DateApplied = 0,
    CompanyName = 1,
    JobTitle = 2,
    Location = 3,
    Status = 4,
    InterestLevel = 5
}

/// <summary>Every filter and sort the spreadsheet page can apply. All filters are optional
/// and combine with AND; a default-constructed filter matches everything.</summary>
public record ApplicationFilter
{
    public ApplicationStatus? Status { get; init; }

    public InterestLevel? InterestLevel { get; init; }

    /// <summary>Inclusive lower bound on <see cref="Entities.Application.DateApplied"/>.</summary>
    public DateOnly? AppliedOnOrAfter { get; init; }

    /// <summary>Inclusive upper bound on <see cref="Entities.Application.DateApplied"/>.</summary>
    public DateOnly? AppliedOnOrBefore { get; init; }

    /// <summary>Case-insensitive substring match against company name.</summary>
    public string? CompanyContains { get; init; }

    /// <summary>Case-insensitive substring match against job title.</summary>
    public string? JobTitleContains { get; init; }

    /// <summary>Case-insensitive substring match against company name *or* job title - the single
    /// search box. Combines with the per-field filters by AND, but is an OR within itself.</summary>
    public string? TextContains { get; init; }

    public bool? FromJobFair { get; init; }

    public ApplicationSortField SortBy { get; init; } = ApplicationSortField.DateApplied;

    public bool SortDescending { get; init; } = true;
}
