namespace ThriveDevCenter.Server.Utilities
{
    public static class EmailHelpers
    {
        /// <summary>
        ///   Checks if an address is probably a no-reply one
        /// </summary>
        /// <param name="email">The address to check</param>
        /// <returns>True if probably a no-reply address</returns>
        public static bool IsNoReplyAddress(string email)
        {
            // ReSharper disable StringLiteralTypo
            if (email.Contains("noreply") || email.Contains("no-reply"))
                return true;

            // ReSharper restore StringLiteralTypo
            return false;
        }
    }
}
