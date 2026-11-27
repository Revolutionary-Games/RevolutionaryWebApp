namespace RevolutionaryWebApp.Client.Utilities;

using System;
using RevolutionaryWebApp.Shared.Models;

public readonly record struct TranslationStringFilter(TranslationState? State, bool ProblemsOnly = false,
    bool PendingSuggestionsOnly = false)
{
    public string QueryParameter
    {
        get
        {
            if (ProblemsOnly)
                return "problemsOnly=true";

            if (PendingSuggestionsOnly)
                return "pendingSuggestionsOnly=true";

            return State.HasValue ? $"state={State.Value}" : string.Empty;
        }
    }

    public string QuerySuffix => this == default ? string.Empty : $"&{QueryParameter}";

    public static TranslationStringFilter FromQuery(string? state, bool problemsOnly, bool pendingSuggestionsOnly)
    {
        if (problemsOnly)
            return new TranslationStringFilter(null, true);

        if (pendingSuggestionsOnly)
            return new TranslationStringFilter(null, PendingSuggestionsOnly: true);

        return Enum.TryParse<TranslationState>(state, true, out var parsed) && Enum.IsDefined(parsed) ?
            new TranslationStringFilter(parsed) :
            default;
    }
}
