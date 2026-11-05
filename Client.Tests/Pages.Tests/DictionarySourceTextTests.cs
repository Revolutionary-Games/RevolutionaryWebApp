namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using Bunit;
using Client.Shared;
using RevolutionaryWebApp.Shared.Models;
using Xunit;

public class DictionarySourceTextTests : BunitContext
{
    [Fact]
    public void HighlightsWholeWordsIgnoringCaseAndPreservesSourceText()
    {
        const string text = "This is a test.\nTEST testing <test>";
        var cut = Render<DictionarySourceText>(parameters => parameters
            .Add(p => p.Text, text)
            .Add(p => p.Terms,
            [
                new DictionaryTermDTO
                {
                    Word = "Test", TargetText = "Testi", DictionaryName = "Finnish terms",
                },
            ]));

        Assert.Equal(3, cut.FindAll(".dictionary-highlight").Count);
        Assert.All(cut.FindAll(".dictionary-tooltip"), tooltip =>
        {
            _ = tooltip;
            Assert.Contains("Testi", tooltip.TextContent);
            Assert.Contains("Word from dictionary Finnish terms", tooltip.TextContent);
        });
        foreach (var tooltip in cut.FindAll(".dictionary-tooltip"))
            tooltip.Remove();
        Assert.Equal(text, cut.Find("#source-text").TextContent);
    }

    [Fact]
    public void IncludesOverlappingAndLanguageSpecificTermsAndRefreshesMatches()
    {
        var cut = Render<DictionarySourceText>(parameters => parameters
            .Add(p => p.Text, "A test case")
            .Add(p => p.Terms,
            [
                new DictionaryTermDTO { Word = "test case", TargetText = "Testitapaus" },
                new DictionaryTermDTO { Word = "Koe", SourceWord = "test", IsLanguageSpecific = true },
            ]));

        var firstTooltip = cut.Find(".dictionary-tooltip");
        Assert.Equal(2, firstTooltip.QuerySelectorAll(".dictionary-tooltip-entry").Length);
        Assert.Contains("Not translated", firstTooltip.TextContent);

        cut.Render(parameters => parameters.Add(p => p.Terms, []));
        Assert.Empty(cut.FindAll(".dictionary-highlight"));
        Assert.Equal("A test case", cut.Find("#source-text").TextContent);
    }
}
