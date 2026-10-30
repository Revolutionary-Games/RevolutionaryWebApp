namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Net;
using System.Net.Http;
using Bunit;
using Client.Pages;
using RevolutionaryWebApp.Shared.Models;
using RichardSzalay.MockHttp;
using Xunit;

public class NotificationInboxTests : BunitContext
{
    [Fact]
    public void PrioritiesHaveDifferentStylesAndMarkAllReadUpdatesTheInbox()
    {
        var http = Services.AddMockHttpClient();
        http.When("/api/v1/userNotifications?offset=0").RespondJson(new[]
        {
            new UserNotificationDTO { Id = 1, Message = "Low", Priority = NotificationPriority.Low },
            new UserNotificationDTO { Id = 2, Message = "Normal" },
            new UserNotificationDTO { Id = 3, Message = "High", Priority = NotificationPriority.High },
        });
        var markRead = http.When(HttpMethod.Post, "/api/v1/userNotifications/readAll")
            .Respond(HttpStatusCode.NoContent);
        var page = Render<NotificationInbox>();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("li").Count));
        Assert.Contains("small", page.FindAll("li")[0].ClassList);
        Assert.DoesNotContain("small", page.FindAll("li")[1].ClassList);
        Assert.Contains("alert-danger", page.FindAll("li")[2].ClassList);
        Assert.Contains("Important", page.FindAll("li")[2].TextContent);
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".list-group-item-info")));
        Assert.Equal(1, http.GetMatchCount(markRead));
    }
}
