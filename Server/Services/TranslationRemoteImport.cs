namespace RevolutionaryWebApp.Server.Services;

using System;
using Models.Translations;
using Shared.Models;

public enum TranslationRemoteImportResult
{
    Unchanged,
    BaselineAdvanced,
    TextChanged,
    Conflict,
}

/// <summary>
///   Applies one source or target-language value from a freshly fetched source repository.
///   Persistence, audit history, and notices are handled by the calling import job.
/// </summary>
public static class TranslationRemoteImport
{
    public static TranslationRemoteImportResult Apply(TranslationValue value, string incomingText, bool fuzzy)
    {
        // A replayed webhook or a changed file with identical text does not alter authorship.
        if (incomingText == value.LastImportedText)
            return TranslationRemoteImportResult.Unchanged;

        // The upstream branch has caught up with a locally edited value. Keep its human attribution.
        if (incomingText == value.Text)
        {
            value.LastImportedText = incomingText;
            value.PendingRevision = null;
            value.PendingSince = null;
            return TranslationRemoteImportResult.BaselineAdvanced;
        }

        // Publishing a PR clears pending work but does not mean the local edit has merged upstream.
        if (value.Text != value.LastImportedText)
            return TranslationRemoteImportResult.Conflict;

        value.Text = incomingText;
        value.LastImportedText = incomingText;
        value.PendingRevision = null;
        value.PendingSince = null;
        value.State = fuzzy || string.IsNullOrWhiteSpace(incomingText) ? TranslationState.NeedsTranslation :
            TranslationState.Translated;
        value.LastEditedByUserId = null;
        value.LastEditedAt = DateTime.UtcNow;
        value.Version = Guid.NewGuid();
        return TranslationRemoteImportResult.TextChanged;
    }
}
