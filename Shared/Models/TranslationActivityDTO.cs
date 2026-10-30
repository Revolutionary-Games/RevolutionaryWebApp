namespace RevolutionaryWebApp.Shared.Models;

using System;

/// <summary>
///   A recent translation edit, source key addition, or discussion comment.
/// </summary>
public sealed class TranslationActivityDTO
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public long? UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public TranslationState? State { get; set; }
    public TranslationState? PreviousState { get; set; }
    public string? PreviousText { get; set; }
}
