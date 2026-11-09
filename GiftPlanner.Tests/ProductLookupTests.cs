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

    [Fact]
    public void Reads_amazon_buy_box_price_and_image()
    {
        const string html = """
            <title>Amazon.com: Slinky Walking Spring Toy : Toys &amp; Games</title>
            <img id="landingImage" data-old-hires="https://m.media-amazon.com/images/I/813.jpg" src="x">
            <div class="a-section aok-hidden twister-plus-buying-options-price-data">{"desktop_buybox_group_1":[{"displayPrice":"$3.59","priceAmount":3.59}]}</div>
            <span class="a-offscreen">$0.22</span>
            """;
        var info = ProductLookup.Parse(html);
        Assert.Equal("Slinky Walking Spring Toy", info.Title);
        Assert.Equal(3.59m, info.Price);
        Assert.Equal("https://m.media-amazon.com/images/I/813.jpg", info.ImageUrl);
    }

    [Fact]
    public void Reads_amazon_price_to_pay_display()
    {
        const string html = """<span class="a-price priceToPay" data-a-size="xl"><span class="a-offscreen"> </span><span aria-hidden="true"><span class="a-price-symbol">$</span><span class="a-price-whole">1,249<span class="a-price-decimal">.</span></span><span class="a-price-fraction">99</span></span></span>""";
        Assert.Equal(1249.99m, ProductLookup.Parse(html).Price);
    }

    [Fact]
    public void Cleans_amazon_brand_and_category_suffix()
    {
        Assert.Equal("Melissa & Doug Water WOW! Animals, Ages 3+",
            ProductLookup.CleanTitle("Melissa & Doug Water WOW! Animals, Ages 3+: Melissa & Doug: 0000772053761: Amazon.com: Toys & Games", null));
    }

    [Fact]
    public void Reads_walmart_page_data_price()
    {
        const string html = """<meta property="og:title" content="3D Puzzle"><script id="__NEXT_DATA__">{"product":{"priceInfo":{"currentPrice":{"currencyUnit":"USD","price":49.99,"priceString":"$49.99"}}}}</script>""";
        Assert.Equal(49.99m, ProductLookup.Parse(html).Price);
    }
}
