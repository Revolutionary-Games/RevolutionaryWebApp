namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using Shared.Notifications;

/// <summary>
///   Shared notification helpers for translations as multiple objects need to send the same notifications (that's why
///   these are a separate class rather than being in the model classes).
/// </summary>
internal static class TranslationNotificationHelper
{
    public static Tuple<SerializedNotification, string> DictionaryUpdated(long dictionaryId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationDictionaryUpdated
        {
            DictionaryId = dictionaryId,
        }, NotificationGroups.TranslationDictionaryUpdatedPrefix + dictionaryId);
    }

    public static Tuple<SerializedNotification, string> DictionaryTermsUpdated(long dictionaryId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationDictionaryTermsUpdated
        {
            DictionaryId = dictionaryId,
        }, NotificationGroups.TranslationDictionaryUpdatedPrefix + dictionaryId);
    }

    public static Tuple<SerializedNotification, string> DictionaryLanguagesUpdated(long dictionaryId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationDictionaryLanguagesUpdated
        {
            DictionaryId = dictionaryId,
        }, NotificationGroups.TranslationDictionaryUpdatedPrefix + dictionaryId);
    }

    public static Tuple<SerializedNotification, string> DictionaryTermUpdated(long dictionaryId, string word,
        string context, string languageCode)
    {
        return new Tuple<SerializedNotification, string>(new TranslationDictionaryTermUpdated
        {
            DictionaryId = dictionaryId,
            Word = word,
            Context = context,
            LanguageCode = languageCode,
        }, NotificationGroups.TranslationDictionaryUpdatedPrefix + dictionaryId);
    }

    public static Tuple<SerializedNotification, string> ProjectUpdated(long projectId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationProjectUpdated
        {
            ProjectId = projectId,
        }, NotificationGroups.TranslationProjectUpdatedPrefix + projectId);
    }

    public static Tuple<SerializedNotification, string> ProjectLanguagesUpdated(long projectId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationProjectLanguagesUpdated
        {
            ProjectId = projectId,
        }, NotificationGroups.TranslationProjectUpdatedPrefix + projectId);
    }

    public static Tuple<SerializedNotification, string> ProjectTranslationsUpdated(long projectId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationProjectTranslationsUpdated
        {
            ProjectId = projectId,
        }, NotificationGroups.TranslationProjectUpdatedPrefix + projectId);
    }

    public static Tuple<SerializedNotification, string> UnitUpdated(long projectId, string key)
    {
        return new Tuple<SerializedNotification, string>(new TranslationUnitUpdated
        {
            ProjectId = projectId,
            Key = key,
        }, NotificationGroups.TranslationKeyScopeUpdated(projectId, key));
    }

    public static Tuple<SerializedNotification, string> ValueUpdated(long projectId, string key, string languageCode)
    {
        return Scoped(new TranslationValueUpdated
        {
            ProjectId = projectId,
            Key = key,
            LanguageCode = languageCode,
        }, projectId, key, languageCode);
    }

    public static Tuple<SerializedNotification, string> HistoryUpdated(long projectId, string key,
        string languageCode)
    {
        return Scoped(new TranslationHistoryUpdated
        {
            ProjectId = projectId,
            Key = key,
            LanguageCode = languageCode,
        }, projectId, key, languageCode);
    }

    public static Tuple<SerializedNotification, string> CommentsUpdated(long projectId, string key,
        string languageCode)
    {
        return Scoped(new TranslationCommentsUpdated
        {
            ProjectId = projectId,
            Key = key,
            LanguageCode = languageCode,
        }, projectId, key, languageCode);
    }

    public static Tuple<SerializedNotification, string> SuggestionsUpdated(long projectId, string key,
        string languageCode)
    {
        return Scoped(new TranslationSuggestionsUpdated
        {
            ProjectId = projectId,
            Key = key,
            LanguageCode = languageCode,
        }, projectId, key, languageCode);
    }

    public static Tuple<SerializedNotification, string> ProjectConflictsUpdated(long projectId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationProjectConflictsUpdated
        {
            ProjectId = projectId,
        }, NotificationGroups.TranslationProjectUpdatedPrefix + projectId);
    }

    public static Tuple<SerializedNotification, string> ProjectSyncStatusUpdated(long projectId)
    {
        return new Tuple<SerializedNotification, string>(new TranslationProjectSyncStatusUpdated
        {
            ProjectId = projectId,
        }, NotificationGroups.TranslationProjectUpdatedPrefix + projectId);
    }

    public static Tuple<SerializedNotification, string> ProjectListUpdated()
    {
        return new Tuple<SerializedNotification, string>(new TranslationProjectListUpdated(),
            NotificationGroups.TranslationProjectListUpdated);
    }

    public static Tuple<SerializedNotification, string> DictionaryListUpdated()
    {
        return new Tuple<SerializedNotification, string>(new TranslationDictionaryListUpdated(),
            NotificationGroups.TranslationDictionaryListUpdated);
    }

    public static Tuple<SerializedNotification, string> DictionaryRequestsUpdated()
    {
        return new Tuple<SerializedNotification, string>(new TranslationDictionaryRequestsUpdated(),
            NotificationGroups.TranslationDictionaryRequestsUpdated);
    }

    private static Tuple<SerializedNotification, string> Scoped(SerializedNotification notification, long projectId,
        string key, string languageCode)
    {
        return new Tuple<SerializedNotification, string>(notification,
            NotificationGroups.TranslationScopeUpdated(projectId, key, languageCode));
    }
}
