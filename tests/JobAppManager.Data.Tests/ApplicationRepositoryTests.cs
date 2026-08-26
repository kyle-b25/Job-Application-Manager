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
            ApplicationStatus.NoResponse, InterestLevel.Green,
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
            Status = ApplicationStatus.NoResponse,
            FromJobFair = true,
            AppliedOnOrBefore = new DateOnly(2026, 8, 3)
        });

        Assert.Equal(new[] { "Alpha Corp" }, companies);
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
