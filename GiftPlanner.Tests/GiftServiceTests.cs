using GiftPlanner.Auth;
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
    private readonly TestDbFactory _factory;
    private readonly int _johnny, _lyla, _groupS, _groupJ;
    private readonly string _linkS, _linkJ;

    public GiftServiceTests()
    {
        _connection.Open();
        var factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        using (var db = factory.CreateDbContext())
        {
            db.Database.EnsureCreated();
            db.Accounts.AddRange(new Account { Id = 1, DisplayName = "Us" }, new Account { Id = 2, DisplayName = "Neighbors" });
            db.SaveChanges();
        }
        _factory = factory;
        _svc = new GiftService(factory, new FixedAccount(1));

        _johnny = _svc.AddKidAsync("Johnny").Result.Id;
        _lyla = _svc.AddKidAsync("Lyla").Result.Id;
        var s = _svc.AddGroupAsync("Grandparents S").Result;
        var j = _svc.AddGroupAsync("Grandparents J").Result;
        (_groupS, _linkS, _groupJ, _linkJ) = (s.Id, s.ShareToken, j.Id, j.ShareToken);
    }

    public void Dispose() => _connection.Dispose();

    private Task<int> AddGift(string title, int? groupId, decimal? price = null) =>
        AddGift(title, [_johnny], groupId is null ? [] : [groupId.Value], price);

    private Task<int> AddGift(string title, int[] kids, int[] groups, decimal? price = null) =>
        _svc.SaveGiftAsync(new GiftEdit { Title = title, KidIds = kids, GroupIds = groups, Price = price });

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

        await _svc.SaveGiftAsync(new GiftEdit { Id = lego, KidIds = [_johnny], Title = "Lego City", GroupIds = [_groupS] });

        var gift = Assert.Single(await _svc.GetGiftsAsync());
        Assert.Equal("Lego City", gift.Title);
        Assert.NotNull(gift.PurchasedAt);
    }

    [Fact]
    public async Task Deleting_a_group_unassigns_its_gifts_and_kills_its_link()
    {
        await AddGift("Lego", _groupS);
        await _svc.DeleteGroupAsync(_groupS);

        Assert.Empty(Assert.Single(await _svc.GetGiftsAsync()).Groups);
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
        await AddGift("Lego", _groupS, 49.99m);
        await AddGift("Book", _groupS, 10m);
        await AddGift("Puzzle", null);

        var summary = await _svc.GetSummaryAsync();
        var s = Assert.Single(summary, x => x.GroupId == _groupS);
        Assert.Equal(2, s.Count);
        Assert.Equal(59.99m, s.Total);
        Assert.Equal("Unassigned", summary[^1].Name);
    }

    [Fact]
    public async Task Gift_offered_to_two_groups_shows_on_both_and_first_buyer_claims_it()
    {
        var game = await AddGift("Board game", [_johnny, _lyla], [_groupS, _groupJ]);

        Assert.Single(await _svc.GetGiftsForLinkAsync(_linkS));
        Assert.Single(await _svc.GetGiftsForLinkAsync(_linkJ));

        Assert.Equal(MarkResult.Ok, await _svc.MarkPurchasedAsync(game, _linkJ));
        Assert.Equal(MarkResult.AlreadyBought, await _svc.MarkPurchasedAsync(game, _linkS));
        Assert.False(await _svc.UnmarkPurchasedAsync(game, _linkS)); // only the buyer can undo
        var gift = Assert.Single(await _svc.GetGiftsAsync());
        Assert.Equal(_groupJ, gift.PurchasedByGroupId);
        Assert.True(await _svc.UnmarkPurchasedAsync(game, _linkJ));
    }

    [Fact]
    public async Task Gift_for_several_kids_shows_under_each_kid_filter()
    {
        await AddGift("Board game", [_johnny, _lyla], [_groupS]);
        await AddGift("Doll", [_lyla], [_groupS]);

        Assert.Single(await _svc.GetGiftsAsync(_johnny));
        Assert.Equal(2, (await _svc.GetGiftsAsync(_lyla)).Count);
    }

    [Fact]
    public async Task Deleting_a_kid_keeps_gifts_shared_with_other_kids()
    {
        await AddGift("Board game", [_johnny, _lyla], [_groupS]);
        await AddGift("Bike", [_johnny], [_groupS]);
        await _svc.DeleteKidAsync(_johnny);

        var left = Assert.Single(await _svc.GetGiftsAsync());
        Assert.Equal("Board game", left.Title);
        Assert.Equal("Lyla", Assert.Single(left.Kids).Name);
    }

    [Fact]
    public async Task Assign_sets_exact_groups()
    {
        var lego = await AddGift("Lego", _groupS);
        await _svc.AssignAsync(lego, [_groupS, _groupJ]);
        Assert.Equal(2, Assert.Single(await _svc.GetGiftsAsync()).Groups.Count);
        await _svc.AssignAsync(lego, []);
        Assert.Empty(Assert.Single(await _svc.GetGiftsAsync()).Groups);
    }

    [Theory]
    [InlineData(new[] { "Lyla" }, 3, "Lyla")]
    [InlineData(new[] { "Lyla", "Greyson" }, 3, "Greyson & Lyla")]
    [InlineData(new[] { "Lyla", "Greyson", "Ava" }, 4, "Ava, Greyson & Lyla")]
    [InlineData(new[] { "Lyla", "Greyson" }, 2, "Everyone")]
    public void Kids_label(string[] names, int total, string expected) =>
        Assert.Equal(expected, GiftService.KidsLabel(names.Select(n => new Kid { Name = n }), total));

    [Fact]
    public async Task Another_account_sees_none_of_our_kids_groups_or_gifts()
    {
        var giftId = await AddGift("Bike", _groupS);
        var neighbors = new GiftService(_factory, new FixedAccount(2));

        Assert.Empty(await neighbors.GetKidsAsync());
        Assert.Empty(await neighbors.GetGroupsAsync());
        Assert.Empty(await neighbors.GetGiftsAsync());
        Assert.Null(await neighbors.FindByUrlAsync("https://x"));
    }

    [Fact]
    public async Task Another_account_cannot_change_or_delete_our_things()
    {
        var giftId = await AddGift("Bike", _groupS);
        var neighbors = new GiftService(_factory, new FixedAccount(2));

        await neighbors.RenameKidAsync(_johnny, "Hacked");
        await neighbors.DeleteKidAsync(_lyla);
        await neighbors.DeleteGroupAsync(_groupJ);
        await neighbors.DeleteGiftAsync(giftId);
        await neighbors.ResetLinkAsync(_groupS);
        Assert.Equal(0, await neighbors.SaveGiftAsync(new GiftEdit { Id = giftId, Title = "Hacked" }));

        Assert.Equal(["Johnny", "Lyla"], (await _svc.GetKidsAsync()).Select(k => k.Name));
        Assert.Equal(2, (await _svc.GetGroupsAsync()).Count);
        var gift = Assert.Single(await _svc.GetGiftsAsync());
        Assert.Equal("Bike", gift.Title);
        Assert.NotNull(await _svc.GetGroupByTokenAsync(_linkS));
    }

    [Fact]
    public async Task A_new_gift_cannot_use_another_accounts_kids_or_groups()
    {
        var neighbors = new GiftService(_factory, new FixedAccount(2));
        var id = await neighbors.SaveGiftAsync(new GiftEdit { Title = "Kite", KidIds = [_johnny], GroupIds = [_groupS] });

        var gift = Assert.Single(await neighbors.GetGiftsAsync());
        Assert.Equal(id, gift.Id);
        Assert.Empty(gift.Kids);
        Assert.Empty(gift.Groups);
        Assert.Empty(await _svc.GetGiftsForLinkAsync(_linkS));
    }

    [Fact]
    public async Task Each_account_starts_empty_and_keeps_its_own_lists()
    {
        var neighbors = new GiftService(_factory, new FixedAccount(2));
        await neighbors.AddKidAsync("Sam");

        Assert.Equal(["Sam"], (await neighbors.GetKidsAsync()).Select(k => k.Name));
        Assert.Equal(["Johnny", "Lyla"], (await _svc.GetKidsAsync()).Select(k => k.Name));
    }
}

internal sealed class FixedAccount(int id) : IAccountContext
{
    public Task<int> GetAccountIdAsync() => Task.FromResult(id);
}

internal sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}
