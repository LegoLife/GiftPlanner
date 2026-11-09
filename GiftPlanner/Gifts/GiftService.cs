using System.Security.Cryptography;
using GiftPlanner.Data;
using Microsoft.EntityFrameworkCore;

namespace GiftPlanner.Gifts;

public sealed record GroupSummary(int? GroupId, string Name, int Count, int Purchased, decimal Total);

public enum MarkResult { Ok, NotFound, AlreadyBought }

/// <summary>All gift rules: a share link only ever sees or touches gifts assigned to its group.</summary>
public sealed class GiftService(IDbContextFactory<AppDbContext> dbFactory)
{
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

    public async Task DeleteKidAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var kid = await db.Kids.Include(k => k.Gifts).FirstOrDefaultAsync(k => k.Id == id);
        if (kid is null) return;
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

    /// <summary>Deletes the group; its gifts become unassigned.</summary>
    public async Task DeleteGroupAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var group = await db.Groups.Include(g => g.Gifts).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return;
        db.Groups.Remove(group);
        await db.SaveChangesAsync();
    }

    // ---------- Admin: gifts ----------

    public async Task<List<Gift>> GetGiftsAsync(int? kidId = null, bool includeArchived = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var q = db.Gifts.Include(g => g.Kid).Include(g => g.Group).AsQueryable();
        if (kidId is not null) q = q.Where(g => g.KidId == kidId);
        if (!includeArchived) q = q.Where(g => !g.Archived);
        return await q.OrderBy(g => g.Kid.Name).ThenBy(g => g.CreatedAt).ToListAsync();
    }

    /// <summary>Adds or updates a gift's details. Never changes purchase state.</summary>
    public async Task SaveGiftAsync(Gift gift)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entity = gift.Id == 0 ? new Gift { KidId = gift.KidId } : await db.Gifts.FindAsync(gift.Id);
        if (entity is null) return;
        entity.KidId = gift.KidId;
        entity.Title = gift.Title.Trim();
        entity.Url = string.IsNullOrWhiteSpace(gift.Url) ? null : gift.Url.Trim();
        entity.Notes = string.IsNullOrWhiteSpace(gift.Notes) ? null : gift.Notes.Trim();
        entity.Price = gift.Price;
        entity.ImageUrl = string.IsNullOrWhiteSpace(gift.ImageUrl) || entity.Url is null ? null : gift.ImageUrl.Trim();
        entity.GroupId = gift.GroupId;
        if (gift.Id == 0) db.Gifts.Add(entity);
        await db.SaveChangesAsync();
        gift.Id = entity.Id;
    }

    public async Task SetImageAsync(int giftId, string imageUrl)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Gifts.Where(g => g.Id == giftId).ExecuteUpdateAsync(s => s.SetProperty(g => g.ImageUrl, imageUrl));
    }

    public async Task AssignAsync(int giftId, int? groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Gifts.Where(g => g.Id == giftId).ExecuteUpdateAsync(s => s.SetProperty(g => g.GroupId, groupId));
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

    public async Task<List<GroupSummary>> GetSummaryAsync()
    {
        var gifts = await GetGiftsAsync();
        return gifts.GroupBy(g => (g.GroupId, Name: g.Group?.Name ?? "Unassigned"))
            .Select(x => new GroupSummary(x.Key.GroupId, x.Key.Name, x.Count(),
                x.Count(g => g.PurchasedAt != null), x.Sum(g => g.Price ?? 0)))
            .OrderBy(s => s.GroupId is null).ThenBy(s => s.Name)
            .ToList();
    }

    /// <summary>Admin override, e.g. to fix a mis-click.</summary>
    public async Task ClearPurchasedAsync(int giftId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Gifts.Where(g => g.Id == giftId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PurchasedAt, (DateTime?)null));
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
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Gifts.Include(g => g.Kid)
            .Where(g => !g.Archived && g.Group != null && g.Group.ShareToken == token)
            .OrderBy(g => g.Kid.Name).ThenBy(g => g.CreatedAt)
            .ToListAsync();
    }

    /// <summary>Marks a gift bought. Only succeeds for gifts assigned to the link's group.</summary>
    public async Task<MarkResult> MarkPurchasedAsync(int giftId, string token)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        // One conditional UPDATE so two relatives clicking at once can't both win.
        var updated = await db.Gifts
            .Where(g => g.Id == giftId && !g.Archived && g.PurchasedAt == null && g.Group != null && g.Group.ShareToken == token)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PurchasedAt, DateTime.UtcNow));
        if (updated == 1) return MarkResult.Ok;
        var visible = await db.Gifts.AnyAsync(g => g.Id == giftId && !g.Archived && g.Group != null && g.Group.ShareToken == token);
        return visible ? MarkResult.AlreadyBought : MarkResult.NotFound;
    }

    public async Task<bool> UnmarkPurchasedAsync(int giftId, string token)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Gifts.Where(g => g.Id == giftId && !g.Archived && g.Group != null && g.Group.ShareToken == token)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.PurchasedAt, (DateTime?)null)) == 1;
    }
}
