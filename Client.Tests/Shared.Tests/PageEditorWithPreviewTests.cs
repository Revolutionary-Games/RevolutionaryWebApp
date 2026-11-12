namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Blazored.Modal;
using Blazored.Modal.Services;
using Bunit;
using Client.Shared;
using Client.Shared.CMS;
using Client.Shared.Modals;
using NSubstitute;
using Xunit;

public class PageEditorWithPreviewTests : BunitContext
{
    [Theory]
    [InlineData("Image", "[center]![image](media:png:00000000-0000-0000-0000-000000000001)[/center]")]
    [InlineData("YouTube", "[youtube]dQw4w9WgXcQ[/youtube]")]
    [InlineData("Table", "| Header |\n| --- |\n| Cell |")]
    [InlineData("Link", "[hello](https://example.com)")]
    public async Task DialogToolsReplaceSavedSelectionAndNotifyParent(string tool, string markdown)
    {
        var modal = Substitute.For<IModalService>();
        var reference = Substitute.For<IModalReference>();
        reference.Result.Returns(Task.FromResult(ModalResult.Ok(markdown)));
        modal.Show<SelectMediaImage>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        modal.Show<InsertYoutubeVideo>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        modal.Show<InsertMarkdownTable>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        modal.Show<InsertPageLink>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        JSInterop.Setup<int[]>("getSelection", _ => true).SetResult([6, 11]);
        JSInterop.SetupVoid("setCaretAndFocus", _ => true);
        ComponentFactories.AddStub<MarkdownRenderer>();
        string? edited = null;
        var editor = Render<PageEditorWithPreview>(parameters => parameters
            .AddCascadingValue(modal)
            .Add(component => component.Id, 1)
            .Add(component => component.OriginalText, "Start hello end")
            .Add(component => component.OnEdited, text => edited = text));

        await editor.InvokeAsync(() => editor.FindAll("button")
            .Single(button => button.TextContent.Trim() == tool).Click());

        var inserted = tool == "Link" ?
            markdown :
            Environment.NewLine + Environment.NewLine + markdown +
            Environment.NewLine + Environment.NewLine;
        Assert.Equal("Start " + inserted + " end", edited);
        Assert.Equal(edited, editor.Find("textarea").GetAttribute("value"));
        Assert.DoesNotContain("Preview (inactive)", editor.Markup);
        Assert.Equal(6 + inserted.Length, JSInterop.Invocations["setCaretAndFocus"].Last().Arguments[1]);
        if (tool == "Link")
        {
            modal.Received(1).Show<InsertPageLink>(Arg.Any<string>(), Arg.Is<ModalParameters>(parameters =>
                parameters.Get<string>(nameof(InsertPageLink.SelectedText)) == "hello"));
        }
    }

    [Fact]
    public async Task CancellingDialogLeavesTextUnchanged()
    {
        var modal = Substitute.For<IModalService>();
        var reference = Substitute.For<IModalReference>();
        reference.Result.Returns(Task.FromResult(ModalResult.Cancel()));
        modal.Show<SelectMediaImage>(Arg.Any<string>(), Arg.Any<ModalParameters>()).Returns(reference);
        JSInterop.Setup<int[]>("getSelection", _ => true).SetResult([2, 2]);
        JSInterop.SetupVoid("setCaretAndFocus", _ => true);
        string? edited = null;
        var editor = Render<PageEditorWithPreview>(parameters => parameters
            .AddCascadingValue(modal)
            .Add(component => component.Id, 1)
            .Add(component => component.OriginalText, "Original")
            .Add(component => component.OnEdited, text => edited = text));

        await editor.InvokeAsync(() => editor.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Image").Click());

        Assert.Null(edited);
        Assert.Equal("Original", editor.Find("textarea").GetAttribute("value"));
    }
}
