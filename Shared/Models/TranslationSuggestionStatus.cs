namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json.Serialization;
using SharedBase.Converters;

/// <summary>
///   Lifecycle status of a community translation suggestion.
/// </summary>
[JsonConverter(typeof(ActualEnumStringConverter))]
public enum TranslationSuggestionStatus
{
    Open,
    Accepted,
    Rejected,
}
