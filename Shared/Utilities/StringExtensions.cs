namespace ThriveDevCenter.Shared.Converters
{
    public static class StringExtensions
    {
        // TODO: change this to the utf-8 truncate character
        private const string TruncateText = "...";

        public static string Truncate(this string str, int length = 30)
        {
            if (str == null)
                return string.Empty;

            if (str.Length <= length)
            {
                return str;
            }

            return str.Substring(0, length - TruncateText.Length) + TruncateText;
        }
    }
}
