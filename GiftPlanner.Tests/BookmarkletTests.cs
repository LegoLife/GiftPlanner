using GiftPlanner.Gifts;

namespace GiftPlanner.Tests;

public class BookmarkletTests
{
    [Fact]
    public void Points_at_this_site_and_is_one_safe_line()
    {
        var js = Bookmarklet.Build("https://gifts.example.com");
        Assert.StartsWith("javascript:", js);
        Assert.Contains("'https://gifts.example.com/'", js);
        Assert.DoesNotContain("\n", js);
        Assert.DoesNotContain("#", js);   // would end the URL early
        Assert.DoesNotContain("\"", js);  // would break the href attribute
    }
}
