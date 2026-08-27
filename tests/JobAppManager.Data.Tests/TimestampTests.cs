using JobAppManager.Core.Enums;
using JobAppManager.Data.Repositories;
using JobAppManager.TestSupport;
using Xunit;

namespace JobAppManager.Data.Tests;

/// <summary>What <c>JobAppContext.StampTimestamps</c> writes, asserted against a clock the test
/// controls rather than a tolerance band around the wall clock.
///
/// The previous versions of these tests could only say "within a second of now" and
/// "UpdatedUtc &gt;= CreatedUtc" - the latter passing even if the timestamp never moved at all.
/// </summary>
public class TimestampTests : IDisposable
{
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 5, 4, 9, 0, 0, TimeSpan.Zero));
    private readonly SqliteTestFixture _fixture;

    public TimestampTests() => _fixture = SqliteTestFixture.WithClock(_clock);

    public void Dispose() => _fixture.Dispose();

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task Add_StampsBothTimestampsWithTheExactCurrentInstant()
    {
        var expected = Now;

        await using var context = _fixture.CreateContext();
        var saved = await new ApplicationRepository(context)
            .AddAsync(TestData.Minimal("Stamp Co", new DateOnly(2026, 5, 1)));

        Assert.Equal(expected, saved.CreatedUtc);
        Assert.Equal(expected, saved.UpdatedUtc);
    }

    [Fact]
    public async Task Update_AdvancesUpdatedUtcAndLeavesCreatedUtcExactlyAlone()
    {
        var createdAt = Now;
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context)
                .AddAsync(TestData.Minimal("Update Co", new DateOnly(2026, 5, 1)));
            id = saved.Id;
        }

        var updatedAt = _clock.Advance(TimeSpan.FromHours(3)).UtcDateTime;

        await using (var context = _fixture.CreateContext())
        {
            var repository = new ApplicationRepository(context);
            var loaded = (await repository.GetByIdAsync(id))!;
            loaded.Location = "Somewhere else";
            await repository.UpdateAsync(loaded);
        }

        await using (var context = _fixture.CreateContext())
        {
            var reloaded = (await context.Applications.FindAsync(id))!;

            // Strictly greater, and by the exact amount the clock moved. The old assertion was
            // ">=", which a stamping bug that never advanced the value would have satisfied.
            Assert.Equal(updatedAt, reloaded.UpdatedUtc);
            Assert.True(reloaded.UpdatedUtc > reloaded.CreatedUtc);
            Assert.Equal(createdAt, reloaded.CreatedUtc);
        }
    }

    [Fact]
    public async Task ChangeStatus_AdvancesUpdatedUtc()
    {
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context)
                .AddAsync(TestData.Minimal("Move Co", new DateOnly(2026, 5, 1)));
            id = saved.Id;
        }

        var movedAt = _clock.AdvanceDays(2).UtcDateTime;

        await using (var context = _fixture.CreateContext())
        {
            await new ApplicationRepository(context)
                .ChangeStatusAsync(id, ApplicationStatus.PhoneScreen);
        }

        await using (var context = _fixture.CreateContext())
        {
            var reloaded = (await context.Applications.FindAsync(id))!;

            // Advancing the pipeline is an edit to the application, not only to its history.
            Assert.Equal(movedAt, reloaded.UpdatedUtc);
        }
    }

    [Fact]
    public async Task StatusHistory_IsStampedAtTheMomentOfTheMove()
    {
        var addedAt = Now;
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context)
                .AddAsync(TestData.Minimal("Timeline Co", new DateOnly(2026, 5, 1)));
            id = saved.Id;
        }

        var movedAt = _clock.AdvanceDays(11).UtcDateTime;

        await using (var context = _fixture.CreateContext())
        {
            await new ApplicationRepository(context)
                .ChangeStatusAsync(id, ApplicationStatus.Interview);
        }

        await using (var context = _fixture.CreateContext())
        {
            var loaded = (await new ApplicationRepository(context).GetByIdAsync(id))!;

            Assert.Equal(
                new[] { addedAt, movedAt },
                loaded.StatusHistory.Select(h => h.ChangedUtc));
        }
    }

    [Fact]
    public async Task GetById_ReturnsStatusHistoryOldestFirst()
    {
        int id;

        await using (var context = _fixture.CreateContext())
        {
            var saved = await new ApplicationRepository(context).AddAsync(
                TestData.Minimal("Ordered Co", new DateOnly(2026, 5, 1), ApplicationStatus.Wishlist));
            id = saved.Id;
        }

        foreach (var status in new[]
                 {
                     ApplicationStatus.Applied,
                     ApplicationStatus.PhoneScreen,
                     ApplicationStatus.Interview,
                     ApplicationStatus.Offer
                 })
        {
            _clock.AdvanceDays(3);

            await using var context = _fixture.CreateContext();
            await new ApplicationRepository(context).ChangeStatusAsync(id, status);
        }

        await using (var context = _fixture.CreateContext())
        {
            var loaded = (await new ApplicationRepository(context).GetByIdAsync(id))!;

            // GetByIdAsync orders the Include by ChangedUtc; the timeline in the editor renders
            // straight off it, so the order is behaviour, not an implementation detail.
            Assert.Equal(
                new[]
                {
                    ApplicationStatus.Wishlist,
                    ApplicationStatus.Applied,
                    ApplicationStatus.PhoneScreen,
                    ApplicationStatus.Interview,
                    ApplicationStatus.Offer
                },
                loaded.StatusHistory.Select(h => h.Status));
        }
    }
}
