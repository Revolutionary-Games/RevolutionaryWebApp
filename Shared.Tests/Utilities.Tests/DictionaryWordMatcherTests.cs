namespace RevolutionaryWebApp.Shared.Tests.Utilities.Tests;

using Shared.Utilities;
using Xunit;

public class DictionaryWordMatcherTests
{
    [Theory]
    [InlineData("Achievement", "Achievements", "Achievement")]
    [InlineData("Achievement", "ACHIEVEMENTS", "ACHIEVEMENT")]
    [InlineData("box", "boxes", "box")]
    [InlineData("class", "classes", "class")]
    [InlineData("brush", "brushes", "brush")]
    [InlineData("match", "matches", "match")]
    [InlineData("colony", "colonies", "colon")]
    [InlineData("colony", "colony", "colony")]
    [InlineData("key", "keys", "key")]
    [InlineData("test case", "test cases", "test case")]
    [InlineData("test", "<test>", "test")]
    [InlineData("Achievements", "Achievements", "Achievements")]
    public void MatchesRegularPluralsWithoutHighlightingTheEnding(string word, string text, string expected)
    {
        var match = Assert.Single(DictionaryWordMatcher.FindMatches(text, word));
        Assert.Equal(expected, match.Value);
    }

    [Theory]
    [InlineData("test", "testing")]
    [InlineData("test", "contests")]
    [InlineData("test", "tests2")]
    [InlineData("test", "testsé")]
    [InlineData("colony", "colonial")]
    [InlineData("Achievement", "Acheivements")]
    [InlineData("", "text")]
    [InlineData(" ", "text")]
    public void DoesNotMatchUnrelatedWords(string word, string text)
    {
        Assert.Empty(DictionaryWordMatcher.FindMatches(text, word));
    }
}
