namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Collections.Generic;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RevolutionaryWebApp.Shared.Models;
using Services;
using Shared;
using Xunit;

public class DictionaryTermEditorTests : BunitContext
{
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
