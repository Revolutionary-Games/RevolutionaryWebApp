namespace ThriveDevCenter.Server.Filters;

public class MyRateLimitOptions
{
    public int GlobalGetLimit { get; set; }
    public int GlobalPostLimit { get; set; }
    public int GlobalWindowSeconds { get; set; }

    public int QueueLimit { get; set; }

    // Short duration limits

    public int LoginLimit { get; set; }
    public int LoginWindowSeconds { get; set; }

    public int RegistrationLimit { get; set; }
    public int RegistrationWindowSeconds { get; set; }

    public int CodeRedeemLimit { get; set; }
    public int CodeRedeemWindowSeconds { get; set; }

    public int ShortWindowQueueLimit { get; set; }

    // Token based / longer limits
    public int EmailVerificationTokens { get; set; }
    public int EmailVerificationRefreshSeconds { get; set; }
    public int EmailVerificationRefreshAmount { get; set; }

    public int CrashReportTokens { get; set; }
    public int CrashReportRefreshSeconds { get; set; }
    public int CrashReportRefreshAmount { get; set; }

    public int StackwalkTokens { get; set; }
    public int StackwalkRefreshSeconds { get; set; }
    public int StackwalkRefreshAmount { get; set; }

    // Logged in limits

    public int UserGlobalGetLimit { get; set; }
    public int UserGlobalPostLimit { get; set; }

    /// <summary>
    ///   When true any requests coming from localhost get to bypass the global limit
    /// </summary>
    public bool AllowUnlimitedFromLocalhost { get; set; }
}
