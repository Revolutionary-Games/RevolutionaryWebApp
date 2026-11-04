namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System.Linq;
using Bunit;
using Client.Shared;
using Xunit;

public class CharacterDiffViewTests : BunitContext
{
    [Theory]
    [InlineData("A cot.", "A cat.", "o", "a")]
    [InlineData("Hello", "Hello!", "", "!")]
    [InlineData("Hello!", "Hello", "!", "")]
    [InlineData("a b", "a  b", "", " ")]
    [InlineData("Hello", "hello", "H", "h")]
    [InlineData("猫です", "犬です", "猫", "犬")]
    [InlineData("Hi 😀!", "Hi 😁!", "😀", "😁")]
    [InlineData("cafe\u0301", "cafe", "e\u0301", "e")]
    [InlineData("same", "same", "", "")]
    [InlineData(null, "new", "", "new")]
    [InlineData("old", null, "old", "")]
    [InlineData("<b>\n old\t", "<b>\n new\t", "old", "new")]
    public void HighlightsOnlyChangedCharactersAndPreservesText(string? oldText, string? newText,
        string deleted, string inserted)
    {
        var view = Render<CharacterDiffView>(parameters => parameters
            .Add(component => component.OldText, oldText)
            .Add(component => component.NewText, newText));

        var panes = view.FindAll(".character-diff-content");
        Assert.Equal(oldText ?? string.Empty, panes[0].TextContent);
        Assert.Equal(newText ?? string.Empty, panes[1].TextContent);
        Assert.Equal(deleted, string.Concat(view.FindAll("del").Select(element => element.TextContent)));
        Assert.Equal(inserted, string.Concat(view.FindAll("ins").Select(element => element.TextContent)));
        Assert.Empty(view.FindAll("b"));
    }
}
