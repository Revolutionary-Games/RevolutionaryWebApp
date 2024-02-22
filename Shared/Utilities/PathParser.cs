namespace RevolutionaryWebApp.Shared.Utilities;

using System.IO;
using System.Linq;

public static class PathParser
{
    // TODO: should be safe to replace with Path.GetDirectoryName
    public static string GetParentPath(string path)
    {
        // TODO: there's probably a more elegant algorithm possible here
        var pathParts = path.Split('/');
        return string.Join('/', pathParts.Take(pathParts.Length - 1));
    }

    public static bool IsExtensionUppercase(string path)
    {
        var extension = Path.GetExtension(path);

        if (string.IsNullOrEmpty(extension))
            return false;

        return extension != extension.ToLowerInvariant();
    }
}
