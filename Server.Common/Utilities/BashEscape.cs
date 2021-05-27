namespace ThriveDevCenter.Server.Common.Utilities
{
    public static class BashEscape
    {
        public static string EscapeForBash(string commandPart)
        {
            if (string.IsNullOrEmpty(commandPart))
                return string.Empty;

            return commandPart.Replace(@"\", @"\\").Replace(@"'", @"\'");

            // return commandPart.Replace(@"\", @"\\").Replace(@"""", @"\""")
            //    .Replace(@"'", @"\'");
        }
    }
}
