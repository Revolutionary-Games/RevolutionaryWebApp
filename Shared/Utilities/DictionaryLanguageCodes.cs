namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

/// <summary>
///   Language equivalence for shared translation dictionaries. Project language codes remain unchanged.
/// </summary>
public static class DictionaryLanguageCodes
{
    // Each group starts with the preferred dictionary spelling. Separator variants are generated automatically.
    private static readonly string[][] AliasGroups =
    [
        ["zh-Hans", "zh-CN"],
        ["zh-Hant", "zh-TW"],
    ];

    private static readonly FrozenDictionary<string, string[]> Aliases = AliasGroups
        .SelectMany(group => group.Select(code => new KeyValuePair<string, string[]>(code, group)))
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    ///   Uses dashes and the preferred alias for display and new dictionary entries.
    /// </summary>
    public static string Normalize(string code)
    {
        var dashed = code.Replace('_', '-');
        return Aliases.TryGetValue(dashed, out var group) ? group[0] : dashed;
    }

    /// <summary>
    ///   Produces a bounded set for indexed database equality queries, including legacy spellings.
    ///   The preferred spelling is first; no cache of arbitrary user-provided codes is retained.
    /// </summary>
    public static string[] GetEquivalentCodes(string code)
    {
        var dashed = code.Replace('_', '-');
        var codes = Aliases.TryGetValue(dashed, out var group) ? group : [dashed];
        return codes.SelectMany(alias => alias.Contains('-') ? new[] { alias, alias.Replace('-', '_') } : [alias])
            .ToArray();
    }
}
