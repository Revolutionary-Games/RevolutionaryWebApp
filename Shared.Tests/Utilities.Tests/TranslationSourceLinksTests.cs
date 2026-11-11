namespace RevolutionaryWebApp.Shared.Tests.Utilities.Tests;

using Shared.Utilities;
using Xunit;

public class TranslationSourceLinksTests
{
    private const string BaseUrl = "https://github.com/Revolutionary-Games/Thrive/blob/master/locales/";

    [Theory]
    [InlineData("../src/benchmark/microbe/CloudBenchmark.tscn:90", null,
        "https://github.com/Revolutionary-Games/Thrive/blob/master/src/benchmark/microbe/CloudBenchmark.tscn#L90")]
    [InlineData("../src/file.cs:12", "{path}?line={line}",
        "https://github.com/Revolutionary-Games/Thrive/blob/master/src/file.cs?line=12")]
    [InlineData("../src/file.cs", null,
        "https://github.com/Revolutionary-Games/Thrive/blob/master/src/file.cs")]
    [InlineData("../src/file.cs", "{path}?line={line}",
        "https://github.com/Revolutionary-Games/Thrive/blob/master/src/file.cs")]
    [InlineData("../src/a b#c?.cs:12", null,
        "https://github.com/Revolutionary-Games/Thrive/blob/master/src/a%20b%23c%3F.cs#L12")]
    [InlineData("../src/a%2Fb.cs:12", "{path}",
        "https://github.com/Revolutionary-Games/Thrive/blob/master/src/a%252Fb.cs")]
    [InlineData("./file.cs:1", null,
        "https://github.com/Revolutionary-Games/Thrive/blob/master/locales/file.cs#L1")]
    public void ResolvesSourceReferences(string location, string? template, string expected)
    {
        Assert.Equal(expected, TranslationSourceLinks.CreateUrl(BaseUrl, template, location));
    }

    [Theory]
    [InlineData(null, null, "file.cs:12")]
    [InlineData("javascript:alert(1)/", null, "file.cs:12")]
    [InlineData("https://example.com/no-trailing-slash", null, "file.cs:12")]
    [InlineData("https://example.com/?query/", null, "file.cs:12")]
    [InlineData("https://example.com/", "https://other.example/{path}", "file.cs:12")]
    [InlineData("https://example.com/", "{path}#L{unknown}", "file.cs:12")]
    [InlineData("https://example.com/", "{path}/{path}", "file.cs:12")]
    [InlineData(BaseUrl, null, "//other.example/file.cs:12")]
    [InlineData(BaseUrl, null, "https://other.example/file.cs:12")]
    [InlineData(BaseUrl, null, "\\other.example\\file.cs:12")]
    [InlineData(BaseUrl, null, "file.cs:0")]
    [InlineData(BaseUrl, null, "file.cs:-1")]
    [InlineData(BaseUrl, null, "file.cs:invalid")]
    [InlineData(BaseUrl, null, ":12")]
    public void InvalidOrUnconfiguredReferencesRemainPlainText(string? baseUrl, string? template, string location)
    {
        Assert.Null(TranslationSourceLinks.CreateUrl(baseUrl, template, location));
    }
}
