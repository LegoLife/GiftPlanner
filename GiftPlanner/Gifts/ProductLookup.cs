using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GiftPlanner.Gifts;

public sealed record ProductInfo(string? Title, decimal? Price, string? ImageUrl = null);

/// <summary>
/// Reads a product page and pulls out its name and price, from schema.org JSON-LD first
/// (most big retailers publish it for Google Shopping), then Open Graph / meta tags.
/// </summary>
public sealed partial class ProductLookup(HttpClient http, ILogger<ProductLookup> log)
{
    private const int MaxBytes = 4 * 1024 * 1024;

    public static void Configure(HttpClient http)
    {
        http.Timeout = TimeSpan.FromSeconds(12);
        // Many stores serve bots an empty shell; a normal browser UA gets the real page.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    /// <returns>null if the page couldn't be fetched; otherwise whatever could be found.</returns>
    public async Task<ProductInfo?> LookupAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        // In the browser-only demo there's no server to fetch store pages from (and CORS would block it).
        if (OperatingSystem.IsBrowser()) return null;
        try
        {
            if (await IsPrivateHostAsync(uri.Host, ct)) return null; // don't let it poke at the home network
            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogInformation("Product lookup {Url} returned {Status}", uri, (int)resp.StatusCode);
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            var buffer = new char[MaxBytes];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
            var info = Parse(new string(buffer, 0, read));
            // Make relative image paths absolute against the final (post-redirect) page address.
            var pageUri = resp.RequestMessage?.RequestUri ?? uri;
            return info with { ImageUrl = Uri.TryCreate(pageUri, info.ImageUrl, out var img) && img.Scheme is "http" or "https" ? img.ToString() : null };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            log.LogInformation("Product lookup {Url} failed: {Message}", uri, ex.Message);
            return null;
        }
    }

    private static async Task<bool> IsPrivateHostAsync(string host, CancellationToken ct)
    {
        var addresses = IPAddress.TryParse(host, out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, ct);
        return addresses.Any(a =>
        {
            if (IPAddress.IsLoopback(a) || a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6UniqueLocal) return true;
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            var b = a.GetAddressBytes();
            return a.AddressFamily == AddressFamily.InterNetwork &&
                   (b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
                    (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
        });
    }

    // ---------- Parsing (public for tests) ----------

    public static ProductInfo Parse(string html)
    {
        string? title = null;
        decimal? price = null;
        string? image = null;

        foreach (Match m in JsonLdRegex().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(WebUtility.HtmlDecode(m.Groups[1].Value).Trim(),
                    new JsonDocumentOptions { AllowTrailingCommas = true });
                var product = FindProduct(doc.RootElement);
                if (product is null) continue;
                title ??= Str(product.Value, "name");
                price ??= PriceOf(product.Value);
                image ??= ImageOf(product.Value);
                if (title is not null && price is not null && image is not null) break;
            }
            catch (JsonException) { /* sites sometimes ship broken JSON-LD */ }
        }

        title ??= Meta(html, "og:title") ?? Meta(html, "twitter:title") ?? TitleTag(html);
        price ??= ParsePrice(Meta(html, "product:price:amount") ?? Meta(html, "og:price:amount") ?? ItemPropPrice(html));

        // Store-specific spots for pages that skip schema.org prices.
        price ??= AmazonPrice(html) ?? WalmartPrice(html);

        image = Meta(html, "og:image") ?? Meta(html, "og:image:secure_url") ?? Meta(html, "twitter:image") ?? image
                ?? AmazonImage(html);

        return new ProductInfo(CleanTitle(title, Meta(html, "og:site_name")), price, image);
    }

    /// <summary>Finds a Product (or the first variant of a ProductGroup) anywhere in the JSON-LD.</summary>
    private static JsonElement? FindProduct(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (FindProduct(item) is { } found) return found;
                return null;
            case JsonValueKind.Object:
                var type = e.TryGetProperty("@type", out var t) ? t.ToString() : "";
                if (type.Contains("ProductGroup"))
                {
                    if (e.TryGetProperty("hasVariant", out var variants) && FindProduct(variants) is { } v)
                        return Str(e, "name") is null ? v : MergeName(e, v);
                    return e;
                }
                if (type.Contains("Product")) return e;
                if (e.TryGetProperty("@graph", out var graph)) return FindProduct(graph);
                if (e.TryGetProperty("mainEntity", out var main)) return FindProduct(main);
                return null;
            default:
                return null;
        }
    }

    /// <summary>The group's name is the clean one; variants often append colour/size.</summary>
    private static JsonElement MergeName(JsonElement group, JsonElement variant)
    {
        if (PriceOf(variant) is not { } p) return group;
        var json = JsonSerializer.Serialize(new { name = Str(group, "name"), image = ImageOf(variant) ?? ImageOf(group), offers = new { price = p } });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Amazon: the buy-box price data, then the big "price to pay" display, then the core price block.</summary>
    private static decimal? AmazonPrice(string html)
    {
        if (Regex.Match(html, @"twister-plus-buying-options-price-data[^>]*>\s*\{[^<]*?""priceAmount""\s*:\s*([\d.]+)") is { Success: true } a)
            return ParsePrice(a.Groups[1].Value);
        if (Regex.Match(html, @"priceToPay[^>]*>.{0,400}?a-price-whole"">\s*([\d,]+).{0,200}?a-price-fraction"">\s*(\d+)", RegexOptions.Singleline) is { Success: true } b)
            return ParsePrice($"{b.Groups[1].Value}.{b.Groups[2].Value}");
        if (Regex.Match(html, @"id=""corePrice(?:Display_desktop)?_feature_div"".{0,3000}?a-offscreen"">\s*(\$[\d,.]+)", RegexOptions.Singleline) is { Success: true } c)
            return ParsePrice(c.Groups[1].Value);
        return null;
    }

    private static string? AmazonImage(string html) =>
        Regex.Match(html, @"data-old-hires=""(https://[^""]+)""") is { Success: true } a ? a.Groups[1].Value
        : Regex.Match(html, @"""hiRes""\s*:\s*""(https://[^""]+)""") is { Success: true } b ? b.Groups[1].Value
        : null;

    /// <summary>Walmart (and Sam's Club) keep the live price in their page data.</summary>
    private static decimal? WalmartPrice(string html) =>
        Regex.Match(html, @"""priceInfo""\s*:\s*\{\s*""currentPrice""\s*:\s*\{[^}]*?""price""\s*:\s*([\d.]+)") is { Success: true } m
            ? ParsePrice(m.Groups[1].Value) : null;

    private static string? ImageOf(JsonElement e)
    {
        if (!e.TryGetProperty("image", out var img)) return null;
        return img.ValueKind switch
        {
            JsonValueKind.String => img.GetString(),
            JsonValueKind.Array => img.EnumerateArray().Select(i => i.ValueKind == JsonValueKind.String ? i.GetString() : Str(i, "url")).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)),
            JsonValueKind.Object => Str(img, "url") ?? Str(img, "contentUrl"),
            _ => null,
        };
    }

    private static decimal? PriceOf(JsonElement product)
    {
        if (!product.TryGetProperty("offers", out var offers)) return null;
        var prices = new List<decimal>();
        CollectPrices(offers, prices);
        return prices.Count == 0 ? null : prices.Min(); // e.g. Sam's member price vs non-member: the lower is what they'll pay
    }

    private static void CollectPrices(JsonElement e, List<decimal> into)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray()) CollectPrices(item, into);
            return;
        }
        if (e.ValueKind != JsonValueKind.Object) return;
        foreach (var key in new[] { "price", "lowPrice" })
            if (e.TryGetProperty(key, out var p) && ParsePrice(p.ToString()) is { } v) into.Add(v);
        if (e.TryGetProperty("priceSpecification", out var spec)) CollectPrices(spec, into);
        if (e.TryGetProperty("offers", out var nested)) CollectPrices(nested, into);
    }

    public static decimal? ParsePrice(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cleaned = PriceCharsRegex().Replace(s, "");
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v > 0
            ? Math.Round(v, 2) : null;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? WebUtility.HtmlDecode(v.GetString()!).Trim() : null;

    private static string? Meta(string html, string name)
    {
        foreach (Match m in MetaTagRegex().Matches(html))
        {
            var tag = m.Value;
            var key = AttrRegex("property").Match(tag) is { Success: true } p ? p.Groups[1].Value
                    : AttrRegex("name").Match(tag) is { Success: true } n ? n.Groups[1].Value
                    : AttrRegex("itemprop").Match(tag) is { Success: true } i ? i.Groups[1].Value : null;
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
            var content = AttrRegex("content").Match(tag);
            if (content.Success) return WebUtility.HtmlDecode(content.Groups[1].Value).Trim();
        }
        return null;
    }

    private static string? ItemPropPrice(string html) =>
        ItemPropPriceRegex().Match(html) is { Success: true } m ? m.Groups[1].Value : null;

    private static string? TitleTag(string html) =>
        TitleRegex().Match(html) is { Success: true } m ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : null;

    /// <summary>Drops trailing " - Samsclub.com" / " | Target" style store names.</summary>
    public static string? CleanTitle(string? title, string? siteName)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        title = WhitespaceRegex().Replace(title, " ").Trim();
        var m = SuffixRegex().Match(title);
        if (m.Success)
        {
            var suffix = m.Groups[2].Value;
            if (suffix.Contains(".com", StringComparison.OrdinalIgnoreCase) ||
                (siteName is not null && suffix.Contains(siteName, StringComparison.OrdinalIgnoreCase)) ||
                suffix.Split(' ').Length <= 2)
                title = m.Groups[1].Value.Trim();
        }
        // Amazon style: "Amazon.com: Thing Name : Toys & Games"
        // Amazon style: "Thing Name: Brand: 0000772053761: Amazon.com: Toys & Games"
        title = Regex.Replace(title, @"(?:\s*:\s*[^:]{1,60}){0,2}\s*:\s*Amazon\.[a-z.]+\s*:\s*[^:]*$", "", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(title, @"^Amazon\.[a-z.]+\s*:", RegexOptions.IgnoreCase))
        {
            title = Regex.Replace(title, @"^Amazon\.[a-z.]+\s*:\s*", "", RegexOptions.IgnoreCase);
            title = Regex.Replace(title, @"\s:\s[^:]+$", "");
        }
        return title.Length > 300 ? title[..300] : title;
    }

    [GeneratedRegex(@"<script[^>]+type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonLdRegex();
    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagRegex();
    private static Regex AttrRegex(string attr) =>
        new($@"\b{attr}\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
    [GeneratedRegex(@"itemprop\s*=\s*[""']price[""'][^>]*?content\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex ItemPropPriceRegex();
    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();
    [GeneratedRegex(@"[^\d.]")]
    private static partial Regex PriceCharsRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"^(.*\S)\s+[-|–—]\s+([^-|–—]+)$")]
    private static partial Regex SuffixRegex();
}
