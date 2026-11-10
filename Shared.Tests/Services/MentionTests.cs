namespace RevolutionaryWebApp.Shared.Tests.Services;

using NSubstitute;
using Shared.Services;
using Shared.Utilities;
using Xunit;

public class MentionTests
{
    [Theory]
    [InlineData("hello @user-name", "<a href=\"/profiles/user-name\">@user-name</a>")]
    [InlineData("`@username`", "<code>@username</code>")]
    [InlineData("[**@username**](https://example.com)", "href=\"https://example.com\"")]
    [InlineData("This is @example's profile", "<a href=\"/profiles/example\">@example</a>")]
    public void MentionsRespectMarkdownStructure(string markdown, string expected)
    {
        var service = new MarkdownService(new HtmlSanitizerService(),
            new MarkdownBbCodeService(Substitute.For<IMediaLinkConverter>()));
        var html = service.MarkdownToHtmlLimited(markdown);
        Assert.Contains(expected, html);
        if (markdown.StartsWith('`') || markdown.StartsWith('['))
            Assert.DoesNotContain("/profiles/", html);
    }

    [Theory]
    [InlineData("user-name_12", true)]
    [InlineData("a", false)]
    [InlineData("user.name", false)]
    [InlineData("user name", false)]
    [InlineData("user\n", false)]
    [InlineData("用户", false)]
    public void UsernamesUseSafeMentionCharacters(string name, bool valid)
    {
        Assert.Equal(valid, Usernames.IsValid(name));
    }

    [Fact]
    public void EmailAddressesAndOverlongNamesAreNotMentions()
    {
        Assert.Empty(Usernames.MentionPattern.Matches("email@example.com @" + new string('a', 51)));
    }
}
