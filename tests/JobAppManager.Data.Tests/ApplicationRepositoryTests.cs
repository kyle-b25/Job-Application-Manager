using JobAppManager.TestSupport;
using JobAppManager.Core.Abstractions;
using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>Query, filter, and sort behaviour. Owns its fixture rather than sharing one, so
/// each test gets a database seeded with exactly the rows it asserts on.</summary>
public class ApplicationRepositoryTests : IDisposable
{
    private readonly SqliteTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task SeedAsync()
    {
        await using var context = _fixture.CreateContext();
        var repository = new ApplicationRepository(context);

        await repository.AddAsync(TestData.Minimal(
            "Alpha Corp", new DateOnly(2026, 8, 1),
            ApplicationStatus.Applied, InterestLevel.Green,
            title: "Backend Engineer", location: "Austin, TX", fromJobFair: true));

        await repository.AddAsync(TestData.Minimal(
            "Beta LLC", new DateOnly(2026, 8, 10),
            ApplicationStatus.Interview, InterestLevel.Yellow,
            title: "Frontend Engineer", location: "Boston, MA"));

        await repository.AddAsync(TestData.Minimal(
            "Gamma Inc", new DateOnly(2026, 8, 5),
            ApplicationStatus.Rejected, InterestLevel.Red,
            title: "Data Engineer", location: "Chicago, IL"));
    }

    private async Task<IReadOnlyList<string>> QueryCompaniesAsync(ApplicationFilter filter)
    {
        await using var context = _fixture.CreateContext();
        var results = await new ApplicationRepository(context).QueryAsync(filter);
        return results.Select(a => a.CompanyName).ToList();
    }

    [Fact]
    public async Task Query_WithDefaultFilter_ReturnsEverythingNewestFirst()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(new ApplicationFilter());

        Assert.Equal(new[] { "Beta LLC", "Gamma Inc", "Alpha Corp" }, companies);
    }

    [Fact]
    public async Task Query_SortsAscending_WhenSortDescendingIsFalse()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(new ApplicationFilter
        {
            SortBy = ApplicationSortField.DateApplied,
            SortDescending = false
        });

        Assert.Equal(new[] { "Alpha Corp", "Gamma Inc", "Beta LLC" }, companies);
    }

    [Theory]
    [InlineData(ApplicationSortField.CompanyName)]
    [InlineData(ApplicationSortField.JobTitle)]
    [InlineData(ApplicationSortField.Location)]
    [InlineData(ApplicationSortField.Status)]
    [InlineData(ApplicationSortField.InterestLevel)]
    [InlineData(ApplicationSortField.DateApplied)]
    public async Task Query_DescendingIsExactReverseOfAscending(ApplicationSortField sortBy)
    {
        await SeedAsync();

        var ascending = await QueryCompaniesAsync(
            new ApplicationFilter { SortBy = sortBy, SortDescending = false });
        var descending = await QueryCompaniesAsync(
            new ApplicationFilter { SortBy = sortBy, SortDescending = true });

        Assert.Equal(3, ascending.Count);
        Assert.Equal(ascending.Reverse().ToList(), descending);
    }

    [Fact]
    public async Task Query_FiltersByStatus()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(
            new ApplicationFilter { Status = ApplicationStatus.Interview });

        Assert.Equal(new[] { "Beta LLC" }, companies);
    }

    [Fact]
    public async Task Query_FiltersByInterestLevel()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(
            new ApplicationFilter { InterestLevel = InterestLevel.Green });

        Assert.Equal(new[] { "Alpha Corp" }, companies);
    }

    [Fact]
    public async Task Query_FiltersByDateRange_Inclusively()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(new ApplicationFilter
        {
            AppliedOnOrAfter = new DateOnly(2026, 8, 5),
            AppliedOnOrBefore = new DateOnly(2026, 8, 10),
            SortDescending = false
        });

        Assert.Equal(new[] { "Gamma Inc", "Beta LLC" }, companies);
    }

    [Fact]
    public async Task Query_FiltersByCompany_CaseInsensitively()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(
            new ApplicationFilter { CompanyContains = "alpha" });

        Assert.Equal(new[] { "Alpha Corp" }, companies);
    }

    [Fact]
    public async Task Query_TextContains_MatchesCompanyOrJobTitle()
    {
        await SeedAsync();

        // Matches on company only - no job title contains "gamma".
        Assert.Equal(
            new[] { "Gamma Inc" },
            await QueryCompaniesAsync(new ApplicationFilter { TextContains = "GAMMA" }));

        // Matches on job title only - no company contains "data".
        Assert.Equal(
            new[] { "Gamma Inc" },
            await QueryCompaniesAsync(new ApplicationFilter { TextContains = "data" }));

        // Matches both sides at once: "Backend"/"Frontend" by title, nothing by company.
        Assert.Equal(
            new[] { "Alpha Corp", "Beta LLC" },
            await QueryCompaniesAsync(new ApplicationFilter
            {
                TextContains = "end",
                SortBy = ApplicationSortField.CompanyName,
                SortDescending = false
            }));
    }

    [Fact]
    public async Task Query_TextContains_CombinesWithOtherFiltersByAnd()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(new ApplicationFilter
        {
            TextContains = "e",
            Status = ApplicationStatus.Rejected
        });

        Assert.Equal(new[] { "Gamma Inc" }, companies);
    }

    [Fact]
    public async Task Query_FiltersByJobTitle_CaseInsensitively()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(
            new ApplicationFilter { JobTitleContains = "DATA" });

        Assert.Equal(new[] { "Gamma Inc" }, companies);
    }

    [Fact]
    public async Task Query_FiltersByJobFairFlag()
    {
        await SeedAsync();

        Assert.Equal(new[] { "Alpha Corp" },
            await QueryCompaniesAsync(new ApplicationFilter { FromJobFair = true }));

        Assert.Equal(2,
            (await QueryCompaniesAsync(new ApplicationFilter { FromJobFair = false })).Count);
    }

    [Fact]
    public async Task Query_CombinesFiltersWithAnd()
    {
        await SeedAsync();

        var companies = await QueryCompaniesAsync(new ApplicationFilter
        {
            Status = ApplicationStatus.Applied,
            FromJobFair = true,
            AppliedOnOrBefore = new DateOnly(2026, 8, 3)
        });

        Assert.Equal(new[] { "Alpha Corp" }, companies);
    }

    [Fact]
    public async Task Query_TreatsWildcardCharactersInTheSearchTermAsLiterals()
    {
        await SeedAsync();

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            await repository.AddAsync(TestData.Minimal("100% Remote Ltd", new DateOnly(2026, 8, 2)));
            await repository.AddAsync(TestData.Minimal("Under_score Inc", new DateOnly(2026, 8, 3)));
        }

        // Unescaped, "%" is the LIKE wildcard and would match all five rows.
        Assert.Equal(
            new[] { "100% Remote Ltd" },
            await QueryCompaniesAsync(new ApplicationFilter { TextContains = "%" }));

        // Unescaped, "_" matches any single character, so "a_p" would match "Alpha Corp".
        Assert.Equal(
            new[] { "Under_score Inc" },
            await QueryCompaniesAsync(new ApplicationFilter { TextContains = "_" }));
    }

    [Fact]
    public async Task Query_TreatsABackslashInTheSearchTermAsALiteral()
    {
        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            await repository.AddAsync(TestData.Minimal(@"Back\slash Co", new DateOnly(2026, 8, 1)));
            await repository.AddAsync(TestData.Minimal("Plain Co", new DateOnly(2026, 8, 2)));
        }

        // The backslash is the escape character, so it has to be escaped before the wildcards
        // are - otherwise it would swallow the escape added immediately after it.
        Assert.Equal(
            new[] { @"Back\slash Co" },
            await QueryCompaniesAsync(new ApplicationFilter { TextContains = @"\" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Query_IgnoresBlankSearchTerms(string term)
    {
        await SeedAsync();

        // A whitespace-only term hits the IsNullOrWhiteSpace guard and must not narrow anything -
        // a search box that clears to spaces should show everything, not nothing.
        var companies = await QueryCompaniesAsync(new ApplicationFilter
        {
            TextContains = term,
            CompanyContains = term,
            JobTitleContains = term
        });

        Assert.Equal(3, companies.Count);
    }

    [Fact]
    public async Task Query_BreaksSortTiesByIdSoOrderIsStable()
    {
        // Three rows sharing a sort key: without the ThenBy(Id) tie-break SQLite is free to
        // return them in any order, and the list would reshuffle between refreshes.
        var sameDay = new DateOnly(2026, 8, 8);

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            await repository.AddAsync(TestData.Minimal("First In", sameDay));
            await repository.AddAsync(TestData.Minimal("Second In", sameDay));
            await repository.AddAsync(TestData.Minimal("Third In", sameDay));
        }

        var ascending = await QueryCompaniesAsync(new ApplicationFilter { SortDescending = false });
        var descending = await QueryCompaniesAsync(new ApplicationFilter { SortDescending = true });

        Assert.Equal(new[] { "First In", "Second In", "Third In" }, ascending);
        Assert.Equal(new[] { "Third In", "Second In", "First In" }, descending);
    }

    [Fact]
    public async Task Query_DoesNotLoadChildren()
    {
        await using (var context = _fixture.CreateContext())
        {
            await new ApplicationRepository(context).AddAsync(TestData.FullApplication());
        }

        await using (var context = _fixture.CreateContext())
        {
            var results = await new ApplicationRepository(context).QueryAsync(new ApplicationFilter());

            // The list page shows no child data, so paying to load it on every keystroke would
            // be waste. The interface documents this; nothing checked it.
            var application = Assert.Single(results);
            Assert.Empty(application.SubmittedItems);
            Assert.Empty(application.Contacts);
            Assert.Empty(application.StatusHistory);
        }
    }

    [Fact]
    public async Task Query_ReturnsEmpty_OnEmptyDatabase()
    {
        Assert.Empty(await QueryCompaniesAsync(new ApplicationFilter()));
    }

    [Fact]
    public async Task GetById_ReturnsNull_WhenMissing()
    {
        await using var context = _fixture.CreateContext();

        Assert.Null(await new ApplicationRepository(context).GetByIdAsync(12345));
    }
}
