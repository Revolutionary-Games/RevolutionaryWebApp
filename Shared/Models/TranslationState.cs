namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json.Serialization;
using SharedBase.Converters;

/// <summary>
///   Review state for a translation or dictionary entry.
/// </summary>
[JsonConverter(typeof(ActualEnumStringConverter))]
public enum TranslationState
{
    NeedsTranslation,
    Translated,
    Verified,
}
