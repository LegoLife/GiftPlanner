using GiftPlanner.Data;
using GiftPlanner.Gifts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GiftPlanner.Tests;

/// <summary>Runs <see cref="GiftService"/> against an in-memory SQLite database.</summary>
public sealed class GiftServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly GiftService _svc;
    private readonly int _johnny, _groupS, _groupJ;
    private readonly string _linkS, _linkJ;

    public GiftServiceTests()
    {
        _connection.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        using (var db = factory.CreateDbContext()) db.Database.EnsureCreated();
        _svc = new GiftService(factory);

        _johnny = _svc.AddKidAsync("Johnny").Result.Id;
        var s = _svc.AddGroupAsync("Grandparents S").Result;
        var j = _svc.AddGroupAsync("Grandparents J").Result;
        (_groupS, _linkS, _groupJ, _linkJ) = (s.Id, s.ShareToken, j.Id, j.ShareToken);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<int> AddGift(string title, int? groupId)
    {
        var gift = new Gift { KidId = _johnny, Title = title, GroupId = groupId };
        await _svc.SaveGiftAsync(gift);
        return gift.Id;
    }

    [Fact]
    public void Each_group_gets_a_distinct_unguessable_link()
    {
        Assert.NotEqual(_linkS, _linkJ);
        Assert.True(_linkS.Length >= 20);
        Assert.DoesNotContain('/', _linkS);
        Assert.DoesNotContain('+', _linkS);
    }

    [Fact]
    public async Task A_link_shows_only_gifts_assigned_to_its_group()
    {
        await AddGift("Lego", _groupS);
        await AddGift("Bike", _groupJ);
        await AddGift("Puzzle", null);

        Assert.Equal(["Lego"], (await _svc.GetGiftsForLinkAsync(_linkS)).Select(g => g.Title));
        Assert.Equal(["Bike"], (await _svc.GetGiftsForLinkAsync(_linkJ)).Select(g => g.Title));
        Assert.Empty(await _svc.GetGiftsForLinkAsync("made-up-token"));
        Assert.Null(await _svc.GetGroupByTokenAsync("made-up-token"));
        Assert.Null(await _svc.GetGroupByTokenAsync(""));
    }

    [Fact]
    public async Task A_link_cannot_mark_or_unmark_another_groups_gift()
    {
        var bike = await AddGift("Bike", _groupJ);
        var unassigned = await AddGift("Puzzle", null);

        Assert.Equal(MarkResult.NotFound, await _svc.MarkPurchasedAsync(bike, _linkS));
        Assert.Equal(MarkResult.NotFound, await _svc.MarkPurchasedAsync(unassigned, _linkS));
        Assert.All(await _svc.GetGiftsAsync(), g => Assert.Null(g.PurchasedAt));

        await _svc.MarkPurchasedAsync(bike, _linkJ);
        Assert.False(await _svc.UnmarkPurchasedAsync(bike, _linkS));
        Assert.NotNull((await _svc.GetGiftsAsync()).Single(g => g.Id == bike).PurchasedAt);
    }

    [Fact]
    public async Task Second_mark_is_told_it_is_already_bought_and_undo_works()
    {
        var lego = await AddGift("Lego", _groupS);

        Assert.Equal(MarkResult.Ok, await _svc.MarkPurchasedAsync(lego, _linkS));
        Assert.Equal(MarkResult.AlreadyBought, await _svc.MarkPurchasedAsync(lego, _linkS));
        Assert.True(await _svc.UnmarkPurchasedAsync(lego, _linkS));
        Assert.Null(Assert.Single(await _svc.GetGiftsAsync()).PurchasedAt);
    }

    [Fact]
    public async Task Resetting_a_link_kills_the_old_one()
    {
        await AddGift("Lego", _groupS);
        var fresh = await _svc.ResetLinkAsync(_groupS);

        Assert.NotEqual(_linkS, fresh);
        Assert.Empty(await _svc.GetGiftsForLinkAsync(_linkS));
        Assert.Single(await _svc.GetGiftsForLinkAsync(fresh));
    }

    [Fact]
    public async Task Editing_a_gift_keeps_its_purchase()
    {
        var lego = await AddGift("Lego", _groupS);
        await _svc.MarkPurchasedAsync(lego, _linkS);

        await _svc.SaveGiftAsync(new Gift { Id = lego, KidId = _johnny, Title = "Lego City", GroupId = _groupS });

        var gift = Assert.Single(await _svc.GetGiftsAsync());
        Assert.Equal("Lego City", gift.Title);
        Assert.NotNull(gift.PurchasedAt);
    }

    [Fact]
    public async Task Deleting_a_group_unassigns_its_gifts_and_kills_its_link()
    {
        await AddGift("Lego", _groupS);
        await _svc.DeleteGroupAsync(_groupS);

        Assert.Null(Assert.Single(await _svc.GetGiftsAsync()).GroupId);
        Assert.Null(await _svc.GetGroupByTokenAsync(_linkS));
    }

    [Fact]
    public async Task Archive_hides_bought_gifts_everywhere()
    {
        var lego = await AddGift("Lego", _groupS);
        await AddGift("Train", _groupS);
        await _svc.MarkPurchasedAsync(lego, _linkS);

        Assert.Equal(1, await _svc.ArchivePurchasedAsync());
        Assert.Equal(["Train"], (await _svc.GetGiftsAsync()).Select(g => g.Title));
        Assert.Equal(["Train"], (await _svc.GetGiftsForLinkAsync(_linkS)).Select(g => g.Title));
    }

    [Fact]
    public async Task Summary_totals_by_group()
    {
        await _svc.SaveGiftAsync(new Gift { KidId = _johnny, Title = "Lego", Price = 49.99m, GroupId = _groupS });
        await _svc.SaveGiftAsync(new Gift { KidId = _johnny, Title = "Book", Price = 10m, GroupId = _groupS });
        await AddGift("Puzzle", null);

        var summary = await _svc.GetSummaryAsync();
        var s = Assert.Single(summary, x => x.GroupId == _groupS);
        Assert.Equal(2, s.Count);
        Assert.Equal(59.99m, s.Total);
        Assert.Equal("Unassigned", summary[^1].Name);
    }

    private sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
