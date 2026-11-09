namespace GiftPlanner.Data;

public class Kid
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Gift> Gifts { get; set; } = [];
}

/// <summary>A set of givers (e.g. one pair of grandparents). Anyone with the group's share link sees only its gifts.</summary>
public class GiverGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Unguessable token in the group's link: /g/{ShareToken}.</summary>
    public string ShareToken { get; set; } = "";
    public List<Gift> Gifts { get; set; } = [];
}

public class Gift
{
    public int Id { get; set; }
    /// <summary>Who it's for: one kid, several, or all of them (e.g. a family board game).</summary>
    public List<Kid> Kids { get; set; } = [];
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    /// <summary>Product picture found on the linked page.</summary>
    public string? ImageUrl { get; set; }
    public string Store => GiftPlanner.Gifts.Stores.FromUrl(Url);
    public decimal? Price { get; set; }
    public string? Notes { get; set; }
    /// <summary>Groups whose link shows this gift. The first group to mark it bought claims it.</summary>
    public List<GiverGroup> Groups { get; set; } = [];
    public DateTime? PurchasedAt { get; set; }
    public int? PurchasedByGroupId { get; set; }
    public GiverGroup? PurchasedByGroup { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
