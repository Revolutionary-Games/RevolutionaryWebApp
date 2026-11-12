namespace RevolutionaryWebApp.Shared.Models;

using System;

public sealed class TranslationProblemDTO
{
    public long Id { get; set; }
    public TranslationProblemKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Ignored { get; set; }
    public long? IgnoredByUserId { get; set; }
    public DateTime? IgnoredAt { get; set; }
}
