using GiftPlanner.Data;
using Microsoft.EntityFrameworkCore;

namespace GiftPlanner.Demo;

/// <summary>Made-up kids and gifts. Share tokens are fixed so giver links survive a reload.</summary>
public static class DemoData
{
    public static async Task SeedAsync(IDbContextFactory<AppDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        Kid maya = new() { Name = "Maya" }, leo = new() { Name = "Leo" }, nora = new() { Name = "Nora" };
        GiverGroup grandparents = new() { Name = "Grandma & Grandpa", ShareToken = "grandma-grandpa" };
        GiverGroup aunt = new() { Name = "Aunt Kate", ShareToken = "aunt-kate" };
        var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var n = 0;

        Gift G(string title, decimal price, string url, string color, string label,
               Kid[] kids, GiverGroup[] groups, bool bought = false) => new()
        {
            Title = title, Price = price, Url = url, ImageUrl = Picture(color, label),
            Kids = [.. kids], Groups = [.. groups], CreatedAt = start.AddMinutes(n++),
            PurchasedAt = bought ? start.AddDays(9) : null, PurchasedByGroup = bought ? groups[0] : null,
        };

        db.Gifts.AddRange(
            G("Watercolor Paint Set, 48 Colors", 24.99m, "https://www.amazon.com/dp/demo1", "#e76f51", "PAINT", [maya], [grandparents]),
            G("Kids Digital Camera", 39.99m, "https://www.walmart.com/ip/demo2", "#2a9d8f", "CAMERA", [maya], [aunt]),
            G("Illustrated Atlas of Animals", 18.50m, "https://www.amazon.com/dp/demo3", "#457b9d", "BOOK", [maya, nora], [grandparents], bought: true),
            G("Remote Control Monster Truck", 44.00m, "https://www.walmart.com/ip/demo4", "#d62828", "RC", [leo], [grandparents]),
            G("Magnetic Building Tiles, 100 pc", 59.99m, "https://www.amazon.com/dp/demo5", "#6a4c93", "TILES", [leo], [aunt]),
            G("Dinosaur Dig Kit", 14.97m, "https://www.samsclub.com/p/demo6", "#bc6c25", "DIG", [leo], [grandparents, aunt], bought: true),
            G("Plush Bunny", 16.99m, "https://www.amazon.com/dp/demo7", "#f4a261", "BUNNY", [nora], [grandparents]),
            G("Wooden Play Kitchen", 89.00m, "https://www.walmart.com/ip/demo8", "#588157", "KITCHEN", [nora], [aunt]),
            G("Board Book Bundle", 21.49m, "https://www.amazon.com/dp/demo9", "#3a86ff", "BOOKS", [nora], [grandparents]));
        await db.SaveChangesAsync();
    }

    private static string Picture(string color, string label) =>
        "data:image/svg+xml," + Uri.EscapeDataString(
            "<svg xmlns='http://www.w3.org/2000/svg' width='400' height='400'><rect width='400' height='400' fill='#f3f4f6'/>" +
            $"<rect x='70' y='90' width='260' height='220' rx='24' fill='{color}'/>" +
            $"<text x='200' y='215' font-family='Arial' font-size='34' font-weight='bold' fill='white' text-anchor='middle'>{label}</text></svg>");
}
