namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RevolutionaryWebApp.Server.Jobs.Pages;
using RevolutionaryWebApp.Server.Models;
using RevolutionaryWebApp.Server.Models.Pages;
using RevolutionaryWebApp.Server.Services;
using Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class RegenerateLosslessMediaPreviewsJobTests
{
    [Fact]
    public async Task RegeneratesBothWebpPreviewsBeforePurgingAndRetriesOnPurgeFailure()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ApplicationDbContext(options);
        var file = new MediaFile("original.png", Guid.NewGuid(), 1, null)
        {
            Processed = true,
            AllowLossyWebpPreviews = true,
        };
        await database.MediaFiles.AddAsync(file);
        await database.SaveChangesAsync();

        using var image = new Image<Rgba32>(AppInfo.MediaResolutionPage + 100, 128);
        using var png = new MemoryStream();
        await image.SaveAsPngAsync(png);
        var originalBytes = png.ToArray();
        var storage = Substitute.For<IMediaStorage>();
        storage.GetObjectContent($"{file.GlobalId}/original.png")
            .Returns(_ => new MemoryStream(originalBytes, writable: false));

        var uploaded = new Dictionary<string, byte[]>();
        storage.UploadFile(Arg.Any<string>(), Arg.Any<Stream>(), "image/webp", false, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                uploaded[call.ArgAt<string>(0)] = ((MemoryStream)call.Arg<Stream>()).ToArray();
                return Task.CompletedTask;
            });

        const string mediaUrl = "https://media.example.com/images/";
        const string apiKey = "test-key";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MediaStorage:Download:URL"] = mediaUrl,
            ["CDN:BunnyCDN:APIKey"] = apiKey,
        }).Build();
        var purged = new List<string>();
        HttpClient CreatePurgeClient()
        {
            return new HttpClient(new PurgeHandler(request =>
            {
                Assert.Equal(2, uploaded.Count);
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/purge", request.RequestUri!.AbsolutePath);
                Assert.Equal(apiKey, Assert.Single(request.Headers.GetValues("AccessKey")));
                purged.Add(QueryHelpers.ParseQuery(request.RequestUri.Query)["url"].ToString());
                return purged.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            }))
            {
                BaseAddress = new Uri("https://api.bunny.net/"),
            };
        }

        using var firstClient = CreatePurgeClient();
        using var retryClient = CreatePurgeClient();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("bunny").Returns(firstClient, retryClient);
        var job = new RegenerateLosslessMediaPreviewsJob(database, storage, factory, configuration,
            NullLogger<RegenerateLosslessMediaPreviewsJob>.Instance);

        await job.Execute(file.Id, CancellationToken.None);
        Assert.Empty(uploaded);
        Assert.Empty(purged);

        file.AllowLossyWebpPreviews = false;
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => job.Execute(file.Id, CancellationToken.None));
        await job.Execute(file.Id, CancellationToken.None);

        var page = $"{file.GlobalId}/page-fit.webp";
        var thumbnail = $"{file.GlobalId}/thumb.webp";
        Assert.Equal(new[] { mediaUrl + page, mediaUrl + page, mediaUrl + thumbnail }, purged);
        var pageInfo = Image.Identify(uploaded[page]);
        var thumbnailInfo = Image.Identify(uploaded[thumbnail]);
        Assert.Equal(AppInfo.MediaResolutionPage, pageInfo.Width);
        Assert.Equal(AppInfo.MediaResolutionThumbnail, thumbnailInfo.Width);
        Assert.Equal(WebpFileFormatType.Lossless, pageInfo.Metadata.GetWebpMetadata().FileFormat);
        Assert.Equal(WebpFileFormatType.Lossless, thumbnailInfo.Metadata.GetWebpMetadata().FileFormat);
    }

    private sealed class PurgeHandler(Func<HttpRequestMessage, HttpStatusCode> process) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(process(request)));
        }
    }
}
