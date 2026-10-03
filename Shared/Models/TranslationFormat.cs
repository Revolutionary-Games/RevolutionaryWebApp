namespace RevolutionaryWebApp.Shared.Models;

using System.Text.Json.Serialization;
using SharedBase.Converters;

/// <summary>
///   Format used by a translation project to store its translation files.
/// </summary>
[JsonConverter(typeof(ActualEnumStringConverter))]
public enum TranslationFormat
{
    Gettext,
    Resx,
    Manual,
}
