namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.Linq;

/// <summary>
///   Parses shareable activity selections, retaining the original single-view URLs.
/// </summary>
public static class TranslationActivityFilter
{
    public static TranslationActivityView[] Types { get; } =
    [
        TranslationActivityView.Edits, TranslationActivityView.Comments,
        TranslationActivityView.Keys, TranslationActivityView.Source, TranslationActivityView.Suggestions,
    ];

    public static bool TryParse(string? types, TranslationActivityView view, out TranslationActivityView[] selected)
    {
        selected = Types;
        if (!Enum.IsDefined(view))
            return false;

        if (types == null)
        {
            selected = view == TranslationActivityView.All ?
                Types :
                view == TranslationActivityView.Edits ?
                    [TranslationActivityView.Edits, TranslationActivityView.Source] :
                    [view];
            return true;
        }

        if (types.Length == 0 || string.Equals(types, "None", StringComparison.OrdinalIgnoreCase))
        {
            selected = [];
            return true;
        }

        var parts = types.Split(',');
        selected = new TranslationActivityView[parts.Length];
        for (var index = 0; index < parts.Length; ++index)
        {
            if (!Enum.TryParse(parts[index], true, out selected[index]) ||
                !Types.Contains(selected[index]))
            {
                return false;
            }
        }

        selected = selected.Distinct().ToArray();
        return true;
    }
}
