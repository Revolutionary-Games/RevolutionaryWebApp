namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DivergentLocalEditConflictsBeforeAndAfterPublishing(bool published)
    {
        var value = new TranslationValue
        {
            Text = "Local edit",
            LastImportedText = "Old upstream",
            LastEditedByUserId = 42,
            PendingRevision = published ? null : 5,
            PendingSince = published ? null : DateTime.UtcNow,
            LastPublishedRevision = published ? 5 : null,
        };

        var result = TranslationRemoteImport.Apply(value, "Other upstream edit", false);

        Assert.Equal(TranslationRemoteImportResult.Conflict, result);
        Assert.Equal("Local edit", value.Text);
        Assert.Equal("Old upstream", value.LastImportedText);
        Assert.Equal(42, value.LastEditedByUserId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingUpstreamEditAdvancesBaselineBeforeAndAfterPublishing(bool published)
    {
        var value = new TranslationValue
        {
            Text = "Shared edit",
            LastImportedText = "Old upstream",
            LastEditedByUserId = 42,
            PendingRevision = published ? null : 5,
            PendingSince = published ? null : DateTime.UtcNow,
            LastPublishedRevision = published ? 5 : null,
        };

        var result = TranslationRemoteImport.Apply(value, "Shared edit", false);

        Assert.Equal(TranslationRemoteImportResult.BaselineAdvanced, result);
        Assert.Null(value.PendingRevision);
        Assert.Null(value.PendingSince);
        Assert.Equal("Shared edit", value.LastImportedText);
        Assert.Equal(42, value.LastEditedByUserId);
    }

    [Fact]
    public void UnchangedUpstreamPreservesPublishedLocalEdit()
    {
        var value = new TranslationValue
        {
            Text = "Local edit",
            LastImportedText = "Old upstream",
            LastPublishedRevision = 5,
            LastEditedByUserId = 42,
        };

        var result = TranslationRemoteImport.Apply(value, "Old upstream", false);

        Assert.Equal(TranslationRemoteImportResult.Unchanged, result);
        Assert.Equal("Local edit", value.Text);
        Assert.Equal("Old upstream", value.LastImportedText);
        Assert.Equal(42, value.LastEditedByUserId);
    }

    [Fact]
    public void PendingStatusOnlyChangeDoesNotConflictWithRemoteTextEdit()
    {
        var value = new TranslationValue
        {
            Text = "Old upstream",
            LastImportedText = "Old upstream",
            State = TranslationState.Verified,
            PendingRevision = 5,
            PendingSince = DateTime.UtcNow,
        };

        var result = TranslationRemoteImport.Apply(value, "Updated upstream", false);

        Assert.Equal(TranslationRemoteImportResult.TextChanged, result);
        Assert.Equal("Updated upstream", value.Text);
        Assert.Equal("Updated upstream", value.LastImportedText);
        Assert.Equal(TranslationState.Translated, value.State);
        Assert.Null(value.PendingRevision);
        Assert.Null(value.PendingSince);
    }
}
