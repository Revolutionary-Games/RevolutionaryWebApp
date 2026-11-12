namespace RevolutionaryWebApp.Shared.Models;

public sealed class TranslationProblemDTO
{
    public long Id { get; set; }
    public TranslationProblemKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool Ignored { get; set; }
}
