namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class PatreonWebhookControllerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task V2MemberUpdateStoresIdsAndEntitledTier()
    {
        const string secret = "webhook-secret";
        const string payload = """
                               {
                                 "data": {
                                   "type": "member",
                                   "id": "member-1",
                                   "attributes": {
                                     "email": "patron@example.com",
                                     "patron_status": "active_patron",
                                     "currently_entitled_amount_cents": 500,
                                     "full_name": "Patron"
                                   },
                                   "relationships": {
                                     "user": { "data": { "type": "user", "id": "user-1" } },
                                     "currently_entitled_tiers": { "data": [{ "type": "tier", "id": "tier-1" }] }
                                   }
                                 },
                                 "included": [
                                   { "type": "user", "id": "user-1", "attributes": { "full_name": "Patron" } },
                                   { "type": "tier", "id": "tier-1", "attributes": { "title": "Dev Builds" } }
                                 ]
                               }
                               """;

        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(V2MemberUpdateStoresIdsAndEntitledTier)).Options;
        await using var database = new NotificationsEnabledDb(options, notifications);
        await database.PatreonSettings.AddAsync(new PatreonSettings
        {
            Active = true,
            WebhookId = "webhook-1",
            WebhookSecret = secret,
            CreatorToken = "creator-token",
        });
        await database.SaveChangesAsync();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        httpContext.Request.Headers["X-Patreon-Event"] = new StringValues("members:update");
        httpContext.Request.Headers["X-Patreon-Signature"] = new StringValues(Convert
            .ToHexString(HMACMD5.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant());

        var controller = new PatreonWebhookController(new XunitLogger<PatreonWebhookController>(output), database,
            Substitute.For<IBackgroundJobClient>());
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.PostWebhook(null);

        Assert.IsType<OkResult>(result);
        var patron = await database.Patrons.SingleAsync();
        Assert.Equal("member-1", patron.PatreonMemberId);
        Assert.Equal("user-1", patron.PatreonUserId);
        Assert.Equal("tier-1", patron.EntitledTierIds);
        Assert.Equal("patron@example.com", patron.Email);
    }
}
