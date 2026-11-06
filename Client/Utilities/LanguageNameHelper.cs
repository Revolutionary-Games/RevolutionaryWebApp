namespace RevolutionaryWebApp.Client.Utilities;

using System;
using System.Collections.Generic;
using System.Globalization;

public static class LanguageNameHelper
{
    private static readonly Dictionary<string, string?> Names = new(StringComparer.Ordinal);

    private static string cachedUICulture = CultureInfo.CurrentUICulture.Name;

    public static string? GetDisplayName(string languageCode)
    {
        // DisplayName depends on the UI culture, so discard names if the user changes it.
        var uiCulture = CultureInfo.CurrentUICulture.Name;
        if (cachedUICulture != uiCulture)
        {
            Names.Clear();
            cachedUICulture = uiCulture;
        }

        if (Names.TryGetValue(languageCode, out var name))
            return name;

        name = TryGetDisplayName(languageCode);
        if (name == null)
        {
            var candidate = languageCode.Replace('_', '-');
            if (candidate != languageCode)
                name = TryGetDisplayName(candidate);

            while (name == null)
            {
                var lastDash = candidate.LastIndexOf('-');
                if (lastDash <= 0)
                    break;

                candidate = candidate[..lastDash];
                name = TryGetDisplayName(candidate);
            }
        }

        Names[languageCode] = name;
        return name;
    }

    private static string? TryGetDisplayName(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return null;

        try
        {
            // ICU can otherwise accept unknown tags and return the tag itself as its name.
            return CultureInfo.GetCultureInfo(languageCode, predefinedOnly: true).DisplayName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
