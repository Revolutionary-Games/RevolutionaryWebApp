namespace RevolutionaryWebApp.Server.Common.Utilities;

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Primitives;

public static class QueryHelpersExtensions
{
    public static Dictionary<string, string> SelectFirstStringValue(this IDictionary<string, StringValues> data)
    {
        return data.Select(p => (p.Key, p.Value.FirstOrDefault())).Where(p => p.Item2 != null)
            .ToDictionary(p => p.Key, p => p.Item2!);
    }
}
