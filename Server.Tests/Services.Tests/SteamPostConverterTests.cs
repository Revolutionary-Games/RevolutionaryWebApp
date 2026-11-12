namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Server.Services;
using Shared.Services;
using Xunit;

public class SteamPostConverterTests
{
    [Theory]
    [InlineData("# Heading\n\n## Subheading\n\n###### Small", "[h1]Heading[/h1]\n\n" +
        "[h2]Subheading[/h2]\n\n[h3]Small[/h3]")]
    [InlineData("**Bold** and *italic* and ~~deleted~~", "[b]Bold[/b] and [i]italic[/i] and [strike]deleted[/strike]")]
    [InlineData("- First\n- **Second**", "[list]\n[*]First\n[*][b]Second[/b]\n[/list]")]
    [InlineData("1. First\n2. Second", "[olist]\n[*]First\n[*]Second\n[/olist]")]
    [InlineData("[center]Centered **text**[/center]", "Centered [b]text[/b]")]
    [InlineData("[color=red]Some **text**[/color]", "Some [b]text[/b]")]
    [InlineData("[center]\n\n# Heading\n\nParagraph\n\n[/center]", "[h1]Heading[/h1]\n\nParagraph")]
    [InlineData("[size=large][color=red]Some **text**[/color][/size]", "Some [b]text[/b]")]
    [InlineData("[![image](/image.png)](/post)",
        "[url=https://example.com/post][img]https://example.com/image.png[/img][/url]")]
    [InlineData("[steam]1779200[/steam]\n\n[thriveItch]\n\nText", "Text")]
    [InlineData("[youtube]PFaX-MuNgfI[/youtube]", "[previewyoutube=PFaX-MuNgfI;full][/previewyoutube]")]
    [InlineData("Before\n\n[youtube]PFaX-MuNgfI[/youtube]\n\nAfter",
        "Before\n\n[previewyoutube=PFaX-MuNgfI;full][/previewyoutube]\n\nAfter")]
    [InlineData("[Link](page:devblog)", "[url=https://cdn.example.com/devblog]Link[/url]")]
    [InlineData("[Link](/relative?a=1&b=2)", "[url=https://example.com/relative?a=1&b=2]Link[/url]")]
    [InlineData("![image](https://images.example.com/image.png)",
        "[img]https://images.example.com/image.png[/img]")]
    [InlineData("![image](/images/image.png)", "[img]https://example.com/images/image.png[/img]")]
    [InlineData("[puImage]2025-07-05[/puImage]",
        "[img]https://cdn.example.com/generated/puBanner/2025-07-05[/img]")]
    [InlineData("![alt](media:png:c815f12d-604c-4e2d-a8ce-6b03992d0046)",
        "[img]https://media.example.com/c815f12d-604c-4e2d-a8ce-6b03992d0046/page-fit.png[/img]")]
    [InlineData("A & B <span>plain text</span>", "A & B plain text")]
    [InlineData("Paragraph one\n\nParagraph two", "Paragraph one\n\nParagraph two")]
    [InlineData("", "")]
    public void Convert_ProducesSteamSyntax(string markdown, string expected)
    {
        Assert.Equal(expected, CreateConverter().Convert(markdown));
    }

    [Fact]
    public void Convert_ResolvesGeneratedImagesWithoutCdn()
    {
        Assert.Equal("[img]https://example.com/live/generated/puBanner/2025-07-05[/img]",
            CreateConverter(string.Empty).Convert("[puImage]2025-07-05[/puImage]"));
    }

    private static SteamPostConverter CreateConverter(string cdnUrl = "https://cdn.example.com")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseUrl"] = "https://example.com/",
            ["CDN:LiveUrl"] = cdnUrl,
            ["MediaStorage:Download:URL"] = "https://media.example.com/",
        }).Build();

        var bbCode = new MarkdownBbCodeService(new MediaLinkConverter(configuration));
        return new SteamPostConverter(new MarkdownService(new HtmlSanitizerService(), bbCode), configuration);
    }
}
