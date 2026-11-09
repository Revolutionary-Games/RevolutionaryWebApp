namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Jobs;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Xunit;

public class GithubTranslationWebhookTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceBranchPushQueuesImportRegardlessOfOpenPullRequest(bool open)
    {
        const string secret = "test-secret";
        const string payload = """
                               {"ref":"refs/heads/master","before":"old-sha","after":"new-sha",
                                "repository":{"full_name":"test/repository"}}
                               """;
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IModelUpdateNotificationSender>());
        await database.GithubWebhooks.AddAsync(new GithubWebhook
        {
            Id = AppInfo.SingleResourceTableRowId, Secret = secret,
        });
        await database.TranslationProjects.AddAsync(new TranslationProject
        {
            Id = 1, Name = "Test", Slug = "test", RepositoryFullName = "test/repository", SourceBranch = "master",
        });
        await database.TranslationPullRequests.AddAsync(new TranslationPullRequest
        {
            ProjectId = 1, Number = 42, Open = open,
        });
        await database.SaveChangesAsync();
        var bytes = Encoding.UTF8.GetBytes(payload);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.Headers["X-GitHub-Event"] = "push";
        context.Request.Headers["X-Hub-Signature-256"] = "sha256=" +
            Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes)).ToLowerInvariant();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var controller = new GithubWebhookController(NullLogger<GithubWebhookController>.Instance, database, jobs)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        Assert.IsType<OkResult>(await controller.ReceiveWebhook());
        jobs.Received(1).Create(Arg.Is<Job>(job => job.Type == typeof(ImportTranslationProjectJob) &&
            (long)job.Args[0] == 1), Arg.Any<IState>());
    }
}
