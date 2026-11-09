namespace GiftPlanner.Gifts;

/// <summary>Turns a product link into a friendly store name for grouping.</summary>
public static class Stores
{
    public const string NoLink = "No link";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["samsclub"] = "Sam's Club", ["walmart"] = "Walmart", ["amazon"] = "Amazon", ["amzn"] = "Amazon",
        ["target"] = "Target", ["bestbuy"] = "Best Buy", ["lego"] = "LEGO", ["costco"] = "Costco",
        ["kohls"] = "Kohl's", ["macys"] = "Macy's", ["barnesandnoble"] = "Barnes & Noble", ["etsy"] = "Etsy",
        ["ebay"] = "eBay", ["homedepot"] = "Home Depot", ["lowes"] = "Lowe's", ["dickssportinggoods"] = "Dick's Sporting Goods",
        ["gamestop"] = "GameStop", ["michaels"] = "Michaels", ["hobbylobby"] = "Hobby Lobby", ["oldnavy"] = "Old Navy",
        ["gap"] = "Gap", ["nike"] = "Nike", ["adidas"] = "adidas", ["apple"] = "Apple", ["nintendo"] = "Nintendo",
        ["playstation"] = "PlayStation", ["melissaanddoug"] = "Melissa & Doug", ["crayola"] = "Crayola", ["meijer"] = "Meijer",
        ["kroger"] = "Kroger", ["bjs"] = "BJ's", ["scholastic"] = "Scholastic", ["ikea"] = "IKEA", ["temu"] = "Temu",
    };

    public static string FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Contains("://") ? url : "https://" + url, UriKind.Absolute, out var uri))
            return NoLink;
        var parts = uri.Host.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return NoLink;
        // shop.lego.com → lego, amazon.co.uk → amazon
        var name = parts.Length >= 3 && parts[^2] is "co" or "com" or "org" or "net" && parts[^1].Length == 2
            ? parts[^3]
            : parts.Length >= 2 ? parts[^2] : parts[0];
        return Known.TryGetValue(name, out var known) ? known : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
