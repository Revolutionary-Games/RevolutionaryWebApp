namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Collections.Generic;
using Bunit;
using Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using Services;
using Xunit;

public class DictionaryTermEditorTests : BunitContext
{
    [Fact]
    public void UntranslatableTermsAreReadOnlyForAcceptedTranslators()
    {
        Services.AddMockHttpClient();
        var userInfo = new CurrentUserInfo();
        userInfo.OnReceivedOurInfo(new UserDTO
        {
            Id = 1, Groups = new CachedUserGroups(GroupType.User), AcceptedTranslator = true,
        });
        Services.AddSingleton(userInfo);
        var cut = Render<DictionaryTermEditor>(parameters => parameters
            .Add(p => p.Term, new DictionaryTermDTO { Word = "Ladle", Untranslatable = true })
            .Add(p => p.LanguageCode, "fi"));

        Assert.True(cut.Find("input").HasAttribute("readonly"));
        Assert.Equal("Ladle", cut.Find("input").GetAttribute("value"));
        Assert.True(cut.Find("textarea").HasAttribute("readonly"));
        Assert.True(cut.Find("select").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("button"));
        Assert.Contains("Do not translate", cut.Markup);
    }

    [Fact]
    public void ParameterUpdatesOnlyNotifyWhenDirtyStateChanges()
    {
        Services.AddMockHttpClient();
        var userInfo = new CurrentUserInfo();
        userInfo.OnReceivedOurInfo(null);
        Services.AddSingleton(userInfo);
        var notifications = new List<(string Word, string Context, bool IsDirty)>();
        var term = new DictionaryTermDTO { Word = "Cat", Context = "animal" };
        var cut = Render<DictionaryTermEditor>(parameters => parameters
            .Add(p => p.Term, term)
            .Add(p => p.LanguageCode, "fi")
            .Add(p => p.OnDirtyChanged, notifications.Add));

        cut.Render(parameters => parameters.Add(p => p.Term, term));
        Assert.Equal(new[] { ("Cat", "animal", false) }, notifications);

        cut.Find("input").Change("Kissa");
        cut.Render(parameters => parameters.Add(p => p.Term, term));
        Assert.Equal(new[] { ("Cat", "animal", false), ("Cat", "animal", true) }, notifications);
        Assert.Equal("Kissa", cut.Find("input").GetAttribute("value"));

        cut.Render(parameters => parameters.Add(p => p.ResetVersion, 1));
        cut.Render(parameters => parameters.Add(p => p.Term, term));
        Assert.Equal(new[] { ("Cat", "animal", false), ("Cat", "animal", true), ("Cat", "animal", false) },
            notifications);
    }
}
