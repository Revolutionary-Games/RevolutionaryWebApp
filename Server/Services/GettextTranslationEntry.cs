namespace RevolutionaryWebApp.Server.Services;

public sealed record GettextTranslationEntry(string Key, string SourceText, string TargetText,
    string? Context, string? PluralKey, string[] SourceLocations, string? Description, bool Fuzzy,
    string[] TargetPluralForms);
