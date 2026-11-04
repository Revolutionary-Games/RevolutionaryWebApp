namespace AutomatedUITests.Tests;

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using RevolutionaryWebApp.Server;
using RevolutionaryWebApp.Server.Authorization;
using RevolutionaryWebApp.Server.Models;
using RevolutionaryWebApp.Server.Services;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using Xunit;

[Collection("Association meeting browser tests")]
public class AssociationMeetingTests : IClassFixture<WebHostServerFixture<Startup>>, IClassFixture<WebDriverFixture>
{
    private readonly WebHostServerFixture server;
    private readonly WebDriverFixture driver;

    public AssociationMeetingTests(WebHostServerFixture<Startup> server, WebDriverFixture driver)
    {
        this.server = server;
        this.driver = driver;
    }

    [Fact]
    public async Task Meeting_ParticipantReceivesPollResultsAndMeetingEndInRealtime()
    {
        var root = server.RootUri;
        var database = server.DatabaseFixture.Database;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        const string password = "Meeting UI test password 123!";
        var admin = CreateUser("meetingadmin" + suffix, password);
        var participant = CreateUser("participant" + suffix, password);
        admin.Groups.Add(await database.UserGroups.SingleAsync(g => g.Id == GroupType.Admin));
        await database.Users.AddRangeAsync(admin, participant);
        await database.AssociationMembers.AddRangeAsync(CreateMember(admin), CreateMember(participant));
        await database.SaveChangesAsync();

        var adminSession = new Session
        {
            UserId = admin.Id,
            CachedUserGroups = await admin.ComputeUserGroups(database),
            LastLoggedIn = DateTime.UtcNow,
        };
        await database.Sessions.AddAsync(adminSession);
        await database.SaveChangesAsync();

        using var adminClient = new HttpClient();
        adminClient.BaseAddress = root;
        adminClient.DefaultRequestHeaders.Add("Cookie",
            $"{AppInfo.SessionCookieName}={adminSession.Id}:{admin.Id}");
        using var scope = server.Host!.Services.CreateScope();
        adminClient.DefaultRequestHeaders.Add("X-CSRF-Token",
            scope.ServiceProvider.GetRequiredService<ITokenGenerator>().GenerateCSRFToken(admin));

        var meetingName = "Association UI meeting " + suffix;
        using (var response = await adminClient.PostAsJsonAsync("api/v1/Meetings", new MeetingDTO
               {
                   Name = meetingName,
                   Description = "Meeting for testing live participant voting",
                   StartsAt = DateTime.UtcNow.AddHours(1),
                   JoinGracePeriod = TimeSpan.FromMinutes(45),
                   ReadAccess = AssociationResourceAccess.AssociationMembers,
                   JoinAccess = AssociationResourceAccess.AssociationMembers,
               }))
        {
            await AssertSuccess(response);
        }

        // The creation API requires a future start. Advance the meeting before opening the participant browser.
        var meeting = await database.Meetings.SingleAsync(m => m.Name == meetingName);
        meeting.StartsAt = DateTime.UtcNow.AddMinutes(-1);
        await database.SaveChangesAsync();
        Assert.True(meeting.StartsAt < DateTime.UtcNow);
        Assert.True(meeting.StartsAt + meeting.JoinGracePeriod > DateTime.UtcNow);
        var meetingPath = $"api/v1/Meetings/{meeting.Id}";
        using (var response = await adminClient.PostAsync(meetingPath + "/join", new StringContent(string.Empty)))
        {
            await AssertSuccess(response);
        }

        var page = await driver.NewPage(root);
        try
        {
            await driver.WaitUntilBlazorIsLoaded(page);
            await page.Locator("#userWidgetLoginLink").ClickAsync();
            await page.Locator(".login-form input[type=email]").FillAsync(participant.Email);
            await page.Locator(".login-form input[type=password]").FillAsync(password);
            await page.Locator("#localLoginButton").ClickAsync();
            await Assertions.Expect(page.Locator("#userWidgetLoggedIn")).ToHaveTextAsync(participant.UserName);

            await page.Locator("#leftAssociationButton").ClickAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Meetings", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = meetingName, Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator(".content h3").First).ToHaveTextAsync("Meeting: " + meetingName);
            var endedStatus = page.Locator(".content li").Filter(new() { HasText = "Ended At:" });
            await Assertions.Expect(endedStatus).ToHaveTextAsync("Ended At: Not ended");
            await page.GetByRole(AriaRole.Button, new() { Name = "Join", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("Joined meeting", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Leave", Exact = true }))
                .ToBeVisibleAsync();

            const string pollTitle = "Approve the proposal";
            using (var response = await adminClient.PostAsJsonAsync(meetingPath + "/polls", new MeetingPollDTO
                   {
                       Title = pollTitle,
                       TiebreakType = VotingTiebreakType.Random,
                       ParsedData = new PollData
                       {
                           SingleChoiceOption = new PollData.SingleChoice(),
                           Choices =
                           {
                               { 1, new PollData.PollChoice(1, "Approve") },
                               { 2, new PollData.PollChoice(2, "Reject") },
                           },
                       },
                   }))
            {
                await AssertSuccess(response);
            }

            // From here on, all participant updates must arrive without navigating or reloading the page.
            var poll = page.Locator(".content div.mb-3").Filter(new LocatorFilterOptions
            {
                Has = page.GetByRole(AriaRole.Heading, new() { Name = "Poll: " + pollTitle, Exact = true }),
            });
            await Assertions.Expect(poll).ToBeVisibleAsync();
            await poll.GetByRole(AriaRole.Radio, new() { Name = "Approve", Exact = true }).CheckAsync();
            await poll.GetByRole(AriaRole.Button, new() { Name = "Vote", Exact = true }).ClickAsync();
            await Assertions.Expect(poll.GetByText("You have already voted in this poll", new() { Exact = true }))
                .ToBeVisibleAsync();

            var pollId = await database.MeetingPolls.Where(p => p.MeetingId == meeting.Id)
                .Select(p => p.PollId).SingleAsync();
            using (var response = await adminClient.PostAsync(meetingPath + $"/polls/{pollId}/close",
                       new StringContent(string.Empty)))
            {
                await AssertSuccess(response);
            }

            await Assertions.Expect(poll.GetByText("This poll closed at", new() { Exact = false })).ToBeVisibleAsync();

            // Closing the poll queues the real Hangfire job that computes and publishes its results.
            await Assertions.Expect(poll.Locator("ol li")).ToHaveTextAsync(new[] { "Approve: 1", "Reject: 0" },
                new() { Timeout = 30000 });
            await Assertions.Expect(poll.GetByText("Total votes: 1", new() { Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(poll.GetByRole(AriaRole.Button, new() { Name = "Vote", Exact = true }))
                .ToHaveCountAsync(0);

            using (var response = await adminClient.PostAsync(meetingPath + "/end", new StringContent(string.Empty)))
            {
                await AssertSuccess(response);
            }

            await Assertions.Expect(endedStatus).Not.ToContainTextAsync("Not ended");
            await Assertions.Expect(endedStatus).ToContainTextAsync("Ended At:");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    private static User CreateUser(string username, string password)
    {
        var user = new User(username + "@example.com", username)
        {
            Local = true,
            PasswordHash = Passwords.CreateSaltedPasswordHash(password),
        };
        user.ComputeNormalizedEmail();
        return user;
    }

    private static AssociationMember CreateMember(User user)
    {
        return new AssociationMember("UI", "Tester", user.Email, DateOnly.FromDateTime(DateTime.UtcNow),
            "Finland", "Helsinki") { User = user };
    }

    private static async Task AssertSuccess(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,
            $"Admin request failed ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    }
}

// Test hosts share PostgreSQL and Hangfire storage. Keep another host from consuming this test's
// result-computation job and publishing its SignalR notification to a different host's clients.
[CollectionDefinition("Association meeting browser tests", DisableParallelization = true)]
public class AssociationMeetingBrowserCollection;
