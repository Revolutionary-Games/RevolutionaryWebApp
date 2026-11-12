namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Blazored.Modal;
using Blazored.Modal.Services;
using BlazorPagination;
using Bunit;
using Client.Shared.Modals;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using NSubstitute;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using RevolutionaryWebApp.Shared.Models.Pages;
using RevolutionaryWebApp.Shared.Services;
using RichardSzalay.MockHttp;
using Services;
using TextCopy;
using Xunit;

public sealed class EditorDialogTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task YoutubeDialogReportsInvalidUrlAndAcceptsShareLink()
    {
        var host = PrepareModalHost();
        IModalReference modal = null!;
        await host.InvokeAsync(() => modal = Services.GetRequiredService<IModalService>()
            .Show<InsertYoutubeVideo>("Embed YouTube video"));
        host.Find("input").Input("https://example.com/watch?v=dQw4w9WgXcQ");
        host.Find("form").Submit();
        Assert.Contains("Enter a valid YouTube", host.Markup);
        Assert.False(modal.Result.IsCompleted);

        host.Find("input").Input("https://youtu.be/dQw4w9WgXcQ?si=shared");
        host.Find("form").Submit();

        var result = await modal.Result;
        Assert.False(result.Cancelled);
        Assert.Equal("[youtube]dQw4w9WgXcQ[/youtube]", result.Data);
    }

    [Fact]
    public async Task LinkDialogUsesSelectedTextAndSupportsInternalPageLinks()
    {
        var host = PrepareModalHost();
        var parameters = new ModalParameters().Add(nameof(InsertPageLink.SelectedText), "About [Thrive]");
        IModalReference modal = null!;
        await host.InvokeAsync(() => modal = Services.GetRequiredService<IModalService>()
            .Show<InsertPageLink>("Insert link", parameters));
        Assert.Equal("About [Thrive]", host.FindAll("input")[0].GetAttribute("value"));

        host.FindAll("input")[1].Input("page:about");
        host.Find("form").Submit();

        var result = await modal.Result;
        var renderer = new MarkdownService(new HtmlSanitizerService(),
            new MarkdownBbCodeService(new ClientMediaLinkConverter()));
        var html = renderer.MarkdownToHtmlLimited(Assert.IsType<string>(result.Data));
        Assert.Contains("href=\"/live/about\"", html);
        Assert.Contains("About [Thrive]</a>", html);
    }

    [Fact]
    public async Task TableDialogCreatesRenderableTableWithRequestedDimensions()
    {
        var host = PrepareModalHost();
        IModalReference modal = null!;
        await host.InvokeAsync(() => modal = Services.GetRequiredService<IModalService>()
            .Show<InsertMarkdownTable>("Insert table"));
        host.FindAll("input")[0].Change(3);
        host.FindAll("input")[1].Change(2);
        host.Find("form").Submit();

        var result = await modal.Result;
        var renderer = new MarkdownService(new HtmlSanitizerService(),
            new MarkdownBbCodeService(new ClientMediaLinkConverter()));
        var html = renderer.MarkdownToHtmlLimited(Assert.IsType<string>(result.Data));
        Assert.Contains("<th>Header 3</th>", html);
        Assert.Equal(6, html.Split("<td>").Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImagePickerNavigatesWithoutChangingPageAndAcceptsSelectedImage(bool centered)
    {
        var http = Services.AddMockHttpClient();
        Services.AddSingleton<CurrentUserInfo>();
        Services.AddSingleton(CSRFMockFactory.Create());
        Services.AddSingleton<NotificationHandler>();
        Services.AddSingleton<ComponentUrlHelper>();
        Services.AddSingleton<IClipboard>(Substitute.For<IClipboard>());
        var converter = new ClientMediaLinkConverter();
        converter.OnReceiveBaseUrl("https://media.example.com/");
        Services.AddSingleton(converter);
        var folder = new MediaFolderDTO
        {
            Id = 1,
            Name = "Images",
            ContentWriteAccess = GroupType.Developer,
        };
        var imageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        http.When(HttpMethod.Get, "/api/v1/MediaFolder/parsePath").WithQueryString("path", string.Empty)
            .RespondJson(new MediaPathParseResult());
        http.When(HttpMethod.Get, "/api/v1/MediaFolder/parsePath").WithQueryString("path", "Images")
            .RespondJson(new MediaPathParseResult { ParentFolder = folder });
        http.When(HttpMethod.Get, "/api/v1/MediaFolder/folders").WithQueryString("parentFolder", string.Empty)
            .RespondJson(new[] { new MediaFolderInfo { Id = 1, Name = "Images" } });
        http.When(HttpMethod.Get, "/api/v1/MediaFolder/folders").WithQueryString("parentFolder", "1")
            .RespondJson(Array.Empty<MediaFolderInfo>());
        http.When(HttpMethod.Get, "/api/v1/MediaFolder/items").WithQueryString("parentFolder", "1")
            .WithQueryString("page", "1").WithQueryString("pageSize", "50").WithQueryString("sortColumn", "Name")
            .RespondJson(new PagedResult<MediaFileInfo>
            {
                CurrentPage = 1,
                PageCount = 1,
                Results =
                [
                    new MediaFileInfo { Id = 1, Name = "photo.png", Processed = true, GlobalId = imageId },
                    new MediaFileInfo { Id = 2, Name = "pending.png", Processed = false },
                ],
            });
        var startUpload = http.When(HttpMethod.Post, "/api/v1/MediaFile/startUpload")
            .RespondJson(new UploadRequestResponse
            {
                UploadUrl = "https://storage.example.com/upload",
                VerifyToken = "upload-token",
            });
        var finishUpload = http.When(HttpMethod.Post, "/api/v1/MediaFile/finishUpload").Respond("text/plain", "OK");
        var host = PrepareModalHost();
        Services.GetRequiredService<CurrentUserInfo>().OnReceivedOurInfo(new UserDTO
        {
            Id = 1,
            Groups = new CachedUserGroups(GroupType.Developer),
        });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("posts/123?page=7&pageSize=1&sortColumn=CreatedAt&sortDirection=Descending");
        var originalUri = navigation.Uri;
        IModalReference modal = null!;
        await host.InvokeAsync(() => modal = Services.GetRequiredService<IModalService>()
            .Show<SelectMediaImage>("Insert image"));
        host.WaitForAssertion(() => Assert.Single(host.FindAll("tbody a")));
        Assert.True(host.FindAll("button").Single(button => button.TextContent.Trim() == "Accept")
            .HasAttribute("disabled"));

        await host.InvokeAsync(() => host.Find("tbody a").Click());
        host.WaitForAssertion(() => Assert.Equal(2, host.FindAll("tbody tr").Count));
        Assert.Equal(originalUri, navigation.Uri);
        Assert.True(host.FindAll("tbody button")[1].HasAttribute("disabled"));

        await host.InvokeAsync(() => host.Find("tbody button").Click());
        await host.InvokeAsync(() => host.Find(".breadcrumb a").Click());
        host.WaitForAssertion(() => Assert.Single(host.FindAll("tbody a")));
        Assert.True(host.FindAll("button").Single(button => button.TextContent.Trim() == "Accept")
            .HasAttribute("disabled"));
        await host.InvokeAsync(() => host.Find("tbody a").Click());
        host.WaitForAssertion(() => Assert.Equal(2, host.FindAll("tbody tr").Count));

        // A dropped file reaches the existing upload and completion flow inside the popup.
        host.Find(".block-container").DragEnter();
        host.WaitForAssertion(() => Assert.Single(host.FindAll("input[type=file]")));
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "registerFileDropArea");
        host.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("image data", "uploaded.png"));
        await host.InvokeAsync(() => host.Find(".block-overlay").Drop());
        host.WaitForAssertion(() => Assert.Equal(1, http.GetMatchCount(finishUpload)));
        Assert.Equal(1, http.GetMatchCount(startUpload));
        Assert.Contains(JSInterop.Invocations, invocation => invocation.Identifier == "putFormFile" &&
            (string?)invocation.Arguments[0] == "uploaded.png");

        await host.InvokeAsync(() => host.Find("tbody button").Click());
        host.Find("input[type=checkbox]").Change(centered);
        Assert.Contains("Selected image: photo.png", host.Markup);
        await host.InvokeAsync(() => host.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Accept").Click());

        var result = await modal.Result;
        var markdown = $"![photo.png](media:png:{imageId})";
        Assert.Equal(centered ? $"[center]{markdown}[/center]" : markdown, result.Data);
        Assert.Equal(originalUri, navigation.Uri);
        Assert.DoesNotContain(JSInterop.Invocations, invocation => invocation.Identifier == "addToHistory");
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return DisposeAsync().AsTask();
    }

    private IRenderedComponent<CascadingBlazoredModal> PrepareModalHost()
    {
        Services.AddBlazoredModal();
        ComponentFactories.AddStub<BlazorPro.BlazorSize.MediaQuery>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Render<CascadingBlazoredModal>(parameters => parameters
            .Add(component => component.UseCustomLayout, true));
    }
}
