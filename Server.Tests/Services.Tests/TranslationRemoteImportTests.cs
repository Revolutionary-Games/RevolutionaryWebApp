namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using Server.Models.Translations;
using Server.Services;
using Shared.Models;
using Xunit;

public class TranslationRemoteImportTests
{
    [Fact]
    public void IdenticalRemoteTextPreservesHumanAttribution()
    {
        var value = new TranslationValue
        {
            Text = "Current",
            LastImportedText = "Current",
            LastEditedByUserId = 42,
        };

        var result = TranslationRemoteImport.Apply(value, "Current", false);

        Assert.Equal(TranslationRemoteImportResult.Unchanged, result);
        Assert.Equal(42, value.LastEditedByUserId);
    }

    [Fact]
    public void DifferentRemoteTextBecomesSystemEdit()
    {
        var value = new TranslationValue
        {
            Text = "Previous",
            LastImportedText = "Previous",
            LastEditedByUserId = 42,
            State = TranslationState.Verified,
        };

        var result = TranslationRemoteImport.Apply(value, "Updated", false);

        Assert.Equal(TranslationRemoteImportResult.TextChanged, result);
        Assert.Equal("Updated", value.Text);
        Assert.Null(value.LastEditedByUserId);
        Assert.Equal(TranslationState.Translated, value.State);
    }

    [Fact]
    public void DivergentPendingEditIsReportedWithoutOverwritingEitherValue()
    {
        var value = new TranslationValue
        {
            Text = "Local edit",
            LastImportedText = "Old upstream",
            LastEditedByUserId = 42,
            PendingRevision = 5,
        };

        var result = TranslationRemoteImport.Apply(value, "Other upstream edit", false);

        Assert.Equal(TranslationRemoteImportResult.Conflict, result);
        Assert.Equal("Local edit", value.Text);
        Assert.Equal("Old upstream", value.LastImportedText);
        Assert.Equal(42, value.LastEditedByUserId);
    }

    [Fact]
    public void MatchingUpstreamEditClearsPendingButKeepsAttribution()
    {
        var value = new TranslationValue
        {
            Text = "Shared edit",
            LastImportedText = "Old upstream",
            LastEditedByUserId = 42,
            PendingRevision = 5,
        };

        var result = TranslationRemoteImport.Apply(value, "Shared edit", false);

        Assert.Equal(TranslationRemoteImportResult.BaselineAdvanced, result);
        Assert.Null(value.PendingRevision);
        Assert.Equal(42, value.LastEditedByUserId);
    }
}
