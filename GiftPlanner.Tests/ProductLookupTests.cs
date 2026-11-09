using GiftPlanner.Gifts;

namespace GiftPlanner.Tests;

public class ProductLookupTests
{
    [Fact]
    public void Reads_product_group_with_member_and_regular_prices()
    {
        // Shape of a Sam's Club page: ProductGroup → hasVariant → offers → priceSpecification[].
        const string html = """
            <html><head><title>Sahara Racer RC Vehicle - Samsclub.com</title>
            <script type="application/ld+json">[{"@context":"https://schema.org/","@type":"ProductGroup",
              "name":"Sahara Racer 1:24 Scale High Speed All-Terrain RC Vehicle",
              "hasVariant":[{"@type":"Product","name":"Sahara Racer (Red)","offers":[{"@type":"Offer",
                "priceSpecification":[{"price":21.98},{"price":19.98,"validForMemberTier":{}}]}]}]}]</script>
            </head></html>
            """;
        var info = ProductLookup.Parse(html);
        Assert.Equal("Sahara Racer 1:24 Scale High Speed All-Terrain RC Vehicle", info.Title);
        Assert.Equal(19.98m, info.Price);
    }

    [Fact]
    public void Reads_plain_product_in_a_graph()
    {
        const string html = """
            <script type="application/ld+json">{"@graph":[{"@type":"WebPage"},
              {"@type":"Product","name":"LEGO City Fire Truck &amp; Station","offers":{"@type":"Offer","price":"49.99"}}]}</script>
            """;
        var info = ProductLookup.Parse(html);
        Assert.Equal("LEGO City Fire Truck & Station", info.Title);
        Assert.Equal(49.99m, info.Price);
    }

    [Fact]
    public void Falls_back_to_meta_tags_and_strips_store_name()
    {
        const string html = """
            <meta property="og:title" content="Wooden Train Set | Target">
            <meta content="1,299.00" property="product:price:amount">
            """;
        var info = ProductLookup.Parse(html);
        Assert.Equal("Wooden Train Set", info.Title);
        Assert.Equal(1299.00m, info.Price);
    }

    [Fact]
    public void Cleans_amazon_titles()
    {
        Assert.Equal("Melissa & Doug Puzzle",
            ProductLookup.CleanTitle("Amazon.com: Melissa & Doug Puzzle : Toys & Games", null));
    }

    [Fact]
    public void Keeps_dashes_that_are_part_of_the_name()
    {
        Assert.Equal("Spider-Man Web Shooter - Deluxe Edition with Lights",
            ProductLookup.CleanTitle("Spider-Man Web Shooter - Deluxe Edition with Lights", null));
    }

    [Fact]
    public void Broken_json_ld_is_ignored()
    {
        var info = ProductLookup.Parse("""<script type="application/ld+json">{oops</script><title>Kite</title>""");
        Assert.Equal("Kite", info.Title);
        Assert.Null(info.Price);
    }
}
