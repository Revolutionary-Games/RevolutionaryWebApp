namespace RevolutionaryWebApp.Shared.Models;

using System;

/// <summary>
///   Optional translation checks enabled for a project.
/// </summary>
[Flags]
public enum TranslationQualityCheck
{
    None = 0,
    CSharpFormatting = 1,
    SentenceConsistency = 2,
    BbCodeTags = 4,
}

public enum TranslationProblemKind
{
    MissingFormatPlaceholder,
    InitialCapitalization,
    EndingPunctuation,
    BbCodeTagSyntax,
}
