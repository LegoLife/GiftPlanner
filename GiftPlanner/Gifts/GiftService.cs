using System.Security.Cryptography;
using GiftPlanner.Data;
using Microsoft.EntityFrameworkCore;

namespace GiftPlanner.Gifts;

public sealed record GroupSummary(int? GroupId, string Name, int Count, int Purchased, decimal Total);

public enum MarkResult { Ok, NotFound, AlreadyBought }

/// <summary>What the gift dialog edits.</summary>
public sealed class GiftEdit
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public string? Notes { get; set; }
    public IReadOnlyCollection<int> KidIds { get; set; } = [];
    public IReadOnlyCollection<int> GroupIds { get; set; } = [];
    public string Store => Stores.FromUrl(Url);

    public static GiftEdit From(Gift g) => new()
    {
        Id = g.Id, Title = g.Title, Url = g.Url, ImageUrl = g.ImageUrl, Price = g.Price, Notes = g.Notes,
        KidIds = g.Kids.Select(k => k.Id).ToHashSet(), GroupIds = g.Groups.Select(x => x.Id).ToHashSet(),
    };
}

/// <summary>All gift rules: a share link only ever sees or touches gifts assigned to its group.</summary>
public sealed class GiftService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>"Greyson", "Greyson & Lyla", "Ava, Greyson & Lyla", or "Everyone" when it's for every kid.</summary>
    public static string KidsLabel(IEnumerable<Kid> kids, int totalKids)
    {
        var names = kids.Select(k => k.Name).Order().ToList();
        if (names.Count == 0) return "No one yet";
        if (names.Count > 1 && names.Count >= totalKids) return "Everyone";
        return names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " & " + names[^1];
    }

    // ---------- Admin: kids ----------

    public async Task<List<Kid>> GetKidsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Kids.OrderBy(k => k.Name).ToListAsync();
    }

    public async Task<Kid> AddKidAsync(string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var kid = new Kid { Name = name.Trim() };
        db.Kids.Add(kid);
        await db.SaveChangesAsync();
        return kid;
    }

    public async Task RenameKidAsync(int id, string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Kids.Where(k => k.Id == id).ExecuteUpdateAsync(s => s.SetProperty(k => k.Name, name.Trim()));
    }

    /// <summary>Deletes the kid and gifts only for them; shared gifts just drop this kid.</summary>
    public async Task DeleteKidAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var kid = await db.Kids.Include(k => k.Gifts).ThenInclude(g => g.Kids).FirstOrDefaultAsync(k => k.Id == id);
        if (kid is null) return;
        db.Gifts.RemoveRange(kid.Gifts.Where(g => g.Kids.Count == 1));
        db.Kids.Remove(kid);
        await db.SaveChangesAsync();
    }

    // ---------- Admin: groups ----------

    public async Task<List<GiverGroup>> GetGroupsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Groups.OrderBy(g => g.Name).ToListAsync();
    }

    public async Task<GiverGroup> AddGroupAsync(string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var group = new GiverGroup { Name = name.Trim(), ShareToken = NewToken() };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    public async Task RenameGroupAsync(int id, string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Groups.Where(g => g.Id == id).ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, name.Trim()));
    }

    /// <summary>Deletes the group; gifts only it could see become unassigned.</summary>
    public async Task DeleteGroupAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var group = await db.Groups.Include(g => g.Gifts).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return;
        db.Groups.Remove(group);
        await db.SaveChangesAsync();
    }

    // ---------- Admin: gifts ----------

    private static IQueryable<Gift> WithLinks(AppDbContext db) =>
        db.Gifts.Include(g => g.Kids).Include(g => g.Groups).Include(g => g.PurchasedByGroup).AsSplitQuery();

    public async Task<List<Gift>> GetGiftsAsync(int? kidId = null, bool includeArchived = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = WithLinks(db);
        if (kidId is not null) q = q.Where(g => g.Kids.Any(k => k.Id == kidId));
        if (!includeArchived) q = q.Where(g => !g.Archived);
        return await q.OrderBy(g => g.CreatedAt).ToListAsync();
    }

    /// <summary>Adds or updates a gift's details and who it's for / who can buy it. Never changes purchase state.</summary>
    public async Task<int> SaveGiftAsync(GiftEdit edit)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = edit.Id == 0
            ? new Gift()
            : await db.Gifts.Include(g => g.Kids).Include(g => g.Groups).FirstOrDefaultAsync(g => g.Id == edit.Id);
        if (entity is null) return 0;
        entity.Title = edit.Title.Trim();
        entity.Url = string.IsNullOrWhiteSpace(edit.Url) ? null : edit.Url.Trim();
        entity.Notes = string.IsNullOrWhiteSpace(edit.Notes) ? null : edit.Notes.Trim();
        entity.Price = edit.Price;
        entity.ImageUrl = string.IsNullOrWhiteSpace(edit.ImageUrl) || entity.Url is null ? null : edit.ImageUrl.Trim();
        entity.Kids = await db.Kids.Where(k => edit.KidIds.Contains(k.Id)).ToListAsync();
        entity.Groups = await db.Groups.Where(g => edit.GroupIds.Contains(g.Id)).ToListAsync();
        if (edit.Id == 0) db.Gifts.Add(entity);
        await db.SaveChangesAsync();
        return edit.Id = entity.Id;
    }

    /// <summary>An active gift with the same link, ignoring the #fragment and a trailing slash.</summary>
    public async Task<Gift?> FindByUrlAsync(string url)
    {
        var key = url.Split('#')[0].TrimEnd('/');
        await using var db = await dbFactory.CreateDbContextAsync();
        var candidates = await db.Gifts.Include(g => g.Kids).Where(g => !g.Archived && g.Url != null && g.Url.StartsWith(key)).ToListAsync();
        return candidates.FirstOrDefault(g => g.Url!.Split('#')[0].TrimEnd('/') == key);
    }

    public async Task SetImageAsync(int giftId, string imageUrl)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Gifts.Where(g => g.Id == giftId).ExecuteUpdateAsync(s => s.SetProperty(g => g.ImageUrl, imageUrl));
    }

    /// <summary>Sets exactly which groups can see (and buy) a gift.</summary>
    public async Task AssignAsync(int giftId, IEnumerable<int> groupIds)
    {
        var ids = groupIds.ToHashSet();
        await using var db = await dbFactory.CreateDbContextAsync();
        var gift = await db.Gifts.Include(g => g.Groups).FirstOrDefaultAsync(g => g.Id == giftId);
        if (gift is null) return;
        gift.Groups = await db.Groups.Where(g => ids.Contains(g.Id)).ToListAsync();
        await db.SaveChangesAsync();
    }

    public async Task DeleteGiftAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Gifts.Where(g => g.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Hides every bought gift (e.g. after Christmas) so next year's list starts clean.</summary>
    public async Task<int> ArchivePurchasedAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Gifts.Where(g => g.PurchasedAt != null && !g.Archived)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.Archived, true));
    }

    /// <summary>Per group: what its link shows. A gift offered to two groups counts in both.</summary>
    public async Task<List<GroupSummary>> GetSummaryAsync()
    {
        var gifts = await GetGiftsAsync();
        var rows = gifts.SelectMany(g => g.Groups.Count == 0
                ? [(GroupId: (int?)null, Name: "Unassigned", Gift: g)]
                : g.Groups.Select(gr => (GroupId: (int?)gr.Id, gr.Name, Gift: g)))
            .GroupBy(x => (x.GroupId, x.Name))
            .Select(x => new GroupSummary(x.Key.GroupId, x.Key.Name, x.Count(),
                x.Count(r => r.Gift.PurchasedAt != null), x.Sum(r => r.Gift.Price ?? 0)));
        return rows.OrderBy(s => s.GroupId is null).ThenBy(s => s.Name).ToList();
    }

    /// <summary>Admin override, e.g. to fix a mis-click.</summary>
    public async Task ClearPurchasedAsync(int giftId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Gifts.Where(g => g.Id == giftId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PurchasedAt, (DateTime?)null)
                                      .SetProperty(g => g.PurchasedByGroupId, (int?)null));
    }

    // ---------- Share links (no login) ----------

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    /// <summary>Invalidates the old link, e.g. if it was forwarded to the wrong person.</summary>
    public async Task<string> ResetLinkAsync(int groupId)
    {
        var token = NewToken();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Groups.Where(g => g.Id == groupId).ExecuteUpdateAsync(s => s.SetProperty(g => g.ShareToken, token));
        return token;
    }

    public async Task<GiverGroup?> GetGroupByTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Groups.FirstOrDefaultAsync(g => g.ShareToken == token);
    }

    public async Task<List<Gift>> GetGiftsForLinkAsync(string token)
    {
        if (string.IsNullOrEmpty(token)) return [];
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Gifts.Include(g => g.Kids).AsSplitQuery()
            .Where(g => !g.Archived && g.Groups.Any(gr => gr.ShareToken == token))
            .OrderBy(g => g.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Marks a gift bought for the link's group. Only works for gifts that group can see.</summary>
    public async Task<MarkResult> MarkPurchasedAsync(int giftId, string token)
    {
        var group = await GetGroupByTokenAsync(token);
        if (group is null) return MarkResult.NotFound;
        await using var db = await dbFactory.CreateDbContextAsync();
        // One conditional UPDATE so two relatives (or two groups) clicking at once can't both win.
        var updated = await db.Gifts
            .Where(g => g.Id == giftId && !g.Archived && g.PurchasedAt == null && g.Groups.Any(gr => gr.Id == group.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PurchasedAt, DateTime.UtcNow)
                                      .SetProperty(g => g.PurchasedByGroupId, group.Id));
        if (updated == 1) return MarkResult.Ok;
        var visible = await db.Gifts.AnyAsync(g => g.Id == giftId && !g.Archived && g.Groups.Any(gr => gr.Id == group.Id));
        return visible ? MarkResult.AlreadyBought : MarkResult.NotFound;
    }

    /// <summary>Undo is only for the group that marked it bought.</summary>
    public async Task<bool> UnmarkPurchasedAsync(int giftId, string token)
    {
        var group = await GetGroupByTokenAsync(token);
        if (group is null) return false;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Gifts.Where(g => g.Id == giftId && !g.Archived && g.PurchasedByGroupId == group.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PurchasedAt, (DateTime?)null)
                                      .SetProperty(g => g.PurchasedByGroupId, (int?)null)) == 1;
    }
}
