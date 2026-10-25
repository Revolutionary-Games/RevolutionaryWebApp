namespace RevolutionaryWebApp.Server.Services;

// We keep all the data pending new features that would need it.
// ReSharper disable NotAccessedPositionalProperty.Global
public sealed record GettextTranslationEntry(string Key, string SourceText, string TargetText,
    string? Context, string? PluralKey, string[] SourceLocations, string? Description, bool Fuzzy,
    string[] TargetPluralForms);

// ReSharper restore NotAccessedPositionalProperty.Global
