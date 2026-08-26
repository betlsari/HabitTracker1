using Xunit;
using Services;

namespace HabitTrackerApi.Tests;

public class SecurityHardeningTests
{
    [Fact]
    public void TextSanitizer_StripsHtmlAndNormalizesWhitespace()
    {
        var input = "  <b>Hello</b>   <script>alert(1)</script>   world  ";

        var result = TextSanitizer.SanitizePlainText(input);

        Assert.Equal("Hello world", result);
    }
}