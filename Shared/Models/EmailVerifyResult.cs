namespace ThriveDevCenter.Shared.Models
{
    /// <summary>
    ///   Result of client verifying an email address. If this is received, it is always a success.
    /// </summary>
    public class EmailVerifyResult
    {
        /// <summary>
        ///   URL to redirect the client to
        /// </summary>
        public string RedirectTo { get; set; }
    }
}
