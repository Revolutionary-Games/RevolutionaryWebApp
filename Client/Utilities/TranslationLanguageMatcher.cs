namespace RevolutionaryWebApp.Client.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using RevolutionaryWebApp.Shared.Models;

public static class TranslationLanguageMatcher
{
    public static TranslationLanguageDTO? FindMatch(IReadOnlyCollection<TranslationLanguageDTO> languages,
        IEnumerable<string> preferredLanguages)
    {
        foreach (var preferred in preferredLanguages)
        {
            if (string.IsNullOrWhiteSpace(preferred))
                continue;

            var candidate = preferred.Replace('_', '-');
            while (true)
            {
                var match = languages.FirstOrDefault(language => string.Equals(
                    language.LanguageCode.Replace('_', '-'), candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;

                var separator = candidate.LastIndexOf('-');
                if (separator <= 0)
                    break;

                candidate = candidate[..separator];
            }
        }

        return null;
    }
}
