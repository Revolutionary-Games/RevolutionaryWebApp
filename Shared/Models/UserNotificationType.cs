namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json.Serialization;
using SharedBase.Converters;

/// <summary>
///   A supported kind of durable user notification.
/// </summary>
[JsonConverter(typeof(ActualEnumStringConverter))]
public enum UserNotificationType
{
    TranslationEdited,
    TranslationUpdated,
    TranslationChangedInRepository,
    TranslationKeyAdded,
    DictionaryTermEdited,
    DictionaryRequestApproved,
    DictionaryRequestRejected,
    SuggestionAccepted,
    TranslationMention,
}
