namespace RevolutionaryWebApp.Shared.Notifications;

/// <summary>
///   Signals that quality warnings changed without changing translation text or its version.
/// </summary>
public sealed class TranslationProblemsUpdated : SerializedNotification
{
    public long ProjectId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
}
