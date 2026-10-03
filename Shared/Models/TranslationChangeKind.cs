namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json.Serialization;
using SharedBase.Converters;

/// <summary>
///   The reason a translation history item was recorded.
/// </summary>
[JsonConverter(typeof(ActualEnumStringConverter))]
public enum TranslationChangeKind
{
    UserEdit,
    SystemImport,
    SourceChanged,
    StateChanged,
    SuggestionAccepted,
}
