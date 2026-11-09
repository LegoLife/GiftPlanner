using GiftPlanner.Gifts;

namespace GiftPlanner.Tests;

public class StoresTests
{
    [Theory]
    [InlineData("https://www.samsclub.com/ip/Sahara-Racer/20612465955?x=1", "Sam's Club")]
    [InlineData("https://www.amazon.com/dp/B000", "Amazon")]
    [InlineData("https://www.amazon.co.uk/dp/B000", "Amazon")]
    [InlineData("https://shop.lego.com/en-us/product/1", "LEGO")]
    [InlineData("target.com/p/thing", "Target")]
    [InlineData("https://www.toysrus.com/x", "Toysrus")]
    [InlineData(null, Stores.NoLink)]
    [InlineData("", Stores.NoLink)]
    public void Store_name_from_link(string? url, string expected) => Assert.Equal(expected, Stores.FromUrl(url));

    [Fact]
    public void Finds_the_product_image()
    {
        Assert.Equal("https://img/og.jpg",
            ProductLookup.Parse("""<meta property="og:image" content="https://img/og.jpg">""").ImageUrl);
        Assert.Equal("https://img/ld.jpg",
            ProductLookup.Parse("""<script type="application/ld+json">{"@type":"Product","name":"X","image":["https://img/ld.jpg"]}</script>""").ImageUrl);
        Assert.Equal("https://img/obj.jpg",
            ProductLookup.Parse("""<script type="application/ld+json">{"@type":"Product","name":"X","image":{"url":"https://img/obj.jpg"}}</script>""").ImageUrl);
    }
}
