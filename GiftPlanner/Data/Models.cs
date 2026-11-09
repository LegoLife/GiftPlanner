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
    public int KidId { get; set; }
    public Kid Kid { get; set; } = null!;
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    /// <summary>Product picture found on the linked page.</summary>
    public string? ImageUrl { get; set; }
    public string Store => GiftPlanner.Gifts.Stores.FromUrl(Url);
    public decimal? Price { get; set; }
    public string? Notes { get; set; }
    public int? GroupId { get; set; }
    public GiverGroup? Group { get; set; }
    public DateTime? PurchasedAt { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
