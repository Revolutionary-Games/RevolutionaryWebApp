namespace RevolutionaryWebApp.Server.Common.Utilities;

public static class BashEscape
{
    public static string EscapeForBash(string commandPart, bool allowVariables = false)
    {
        if (string.IsNullOrEmpty(commandPart))
            return string.Empty;

        var result = commandPart.Replace(@"\", @"\\").Replace(@"""", @"\""");

        if (!allowVariables)
            result = result.Replace("$", @"\$");

        return result;
    }
}
