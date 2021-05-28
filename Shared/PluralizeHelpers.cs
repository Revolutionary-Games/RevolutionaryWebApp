namespace ThriveDevCenter.Shared
{
    public static class PluralizeHelpers
    {
        public static string PrintCount(this string value, long count)
        {
            return $"{count} {value.Pluralize(count)}";
        }

        public static string Pluralize(this string value, long count)
        {
            if (count == 1)
                return value;

            // TODO: more intelligent pluralize rules
            return value + 's';
        }
    }
}
