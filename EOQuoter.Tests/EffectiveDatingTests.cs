using EOQuoter.Data;
using EOQuoter.Data.Entities;
using EOQuoter.Domain;

namespace EOQuoter.Tests;

/// <summary>
/// The temporal guard itself: effective_date &lt;= asOf, latest wins. The failure being prevented
/// is silent repricing — a rate posted 6-24 must never price a 2-14 submission.
/// </summary>
public class EffectiveDatingTests : IDisposable
{
    private readonly SqliteFixture fixture = new();
    private static readonly DateOnly V1 = new(2025, 1, 1);
    private static readonly DateOnly V2 = new(2026, 4, 1);

    public EffectiveDatingTests()
    {
        using var db = fixture.CreateContext();
        db.Rates.AddRange(
            new RateRow { ProfessionClass = (int)ProfessionClass.Accountant, RevenueBand = (int)RevenueBand.From250KTo1M, BaseRate = 2100m, EffectiveDate = V1 },
            new RateRow { ProfessionClass = (int)ProfessionClass.Accountant, RevenueBand = (int)RevenueBand.From250KTo1M, BaseRate = 2268m, EffectiveDate = V2 });
        db.Appetite.AddRange(
            new AppetiteRow { ProfessionClass = (int)ProfessionClass.RealEstateAgent, IsWritten = true, EffectiveDate = V1 },
            new AppetiteRow { ProfessionClass = (int)ProfessionClass.RealEstateAgent, IsWritten = false, EffectiveDate = new DateOnly(2026, 1, 1) });
        db.SaveChanges();
    }

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Quote_dated_before_the_new_version_prices_at_the_old_rate()
    {
        using var db = fixture.CreateContext();
        var source = new SqlRatingDataSource(db);

        // V2 exists in the table by now — but it was NOT in force on 2026-02-14.
        var rate = await source.GetBaseRateAsync(ProfessionClass.Accountant, RevenueBand.From250KTo1M, new DateOnly(2026, 2, 14));

        Assert.Equal(2100m, rate!.Value.BaseRate);
        Assert.Equal(V1, rate.Value.EffectiveDate);
    }

    [Fact]
    public async Task Quote_dated_on_or_after_the_new_version_picks_it_up()
    {
        using var db = fixture.CreateContext();
        var source = new SqlRatingDataSource(db);

        var onTheDay = await source.GetBaseRateAsync(ProfessionClass.Accountant, RevenueBand.From250KTo1M, V2);
        var later = await source.GetBaseRateAsync(ProfessionClass.Accountant, RevenueBand.From250KTo1M, new DateOnly(2026, 6, 15));

        Assert.Equal(2268m, onTheDay!.Value.BaseRate);
        Assert.Equal(2268m, later!.Value.BaseRate);
    }

    [Fact]
    public async Task Quote_dated_before_any_version_finds_no_rate()
    {
        using var db = fixture.CreateContext();
        var source = new SqlRatingDataSource(db);

        var rate = await source.GetBaseRateAsync(ProfessionClass.Accountant, RevenueBand.From250KTo1M, new DateOnly(2024, 12, 31));

        Assert.Null(rate); // no row in force — an error to surface, never a default
    }

    [Fact]
    public async Task Appetite_exit_is_effective_dated_history_stays_answerable()
    {
        using var db = fixture.CreateContext();
        var source = new SqlEligibilityDataSource(db);

        // Written in November 2025; pulled January 2026. Both answers must be true.
        Assert.True(await source.IsClassInAppetiteAsync(ProfessionClass.RealEstateAgent, new DateOnly(2025, 11, 15)));
        Assert.False(await source.IsClassInAppetiteAsync(ProfessionClass.RealEstateAgent, new DateOnly(2026, 2, 15)));
        // And a class with no row at all was never written.
        Assert.False(await source.IsClassInAppetiteAsync(ProfessionClass.Lawyer, new DateOnly(2026, 2, 15)));
    }
}
