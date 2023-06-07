namespace ThriveDevCenter.Server.Filters;

public class MyRateLimitOptions
{
    public int GlobalGetLimit { get; set; } = 300;
    public int GlobalPostLimit { get; set; } = 100;
    public int GlobalWindowSeconds { get; set; } = 500;

    public int QueueLimit { get; set; } = 0;

    // Short duration limits

    public int LoginLimit { get; set; } = 10;
    public int LoginWindowSeconds { get; set; } = 60;

    public int RegistrationLimit { get; set; } = 5;
    public int RegistrationWindowSeconds { get; set; } = 60;

    public int CodeRedeemLimit { get; set; } = 10;
    public int CodeRedeemWindowSeconds { get; set; } = 60;

    /// <summary>
    ///   How many requests in short requests are allowed to wait while over the request limit. Needs to default to 0
    ///   for tests to work correctly.
    /// </summary>
    public int ShortWindowQueueLimit { get; set; } = 0;

    // Token based / longer limits
    public int EmailVerificationTokens { get; set; } = 10;
    public int EmailVerificationRefreshSeconds { get; set; } = 500;
    public int EmailVerificationRefreshAmount { get; set; } = 1;

    public int CrashReportTokens { get; set; } = 10;
    public int CrashReportRefreshSeconds { get; set; } = 300;
    public int CrashReportRefreshAmount { get; set; } = 2;

    public int StackwalkTokens { get; set; } = 20;
    public int StackwalkRefreshSeconds { get; set; } = 60;
    public int StackwalkRefreshAmount { get; set; } = 2;

    // Logged in limits

    public int UserGlobalGetLimit { get; set; } = 1000;
    public int UserGlobalPostLimit { get; set; } = 800;

    /// <summary>
    ///   When true any requests coming from localhost get to bypass the global limit
    /// </summary>
    public bool AllowUnlimitedFromLocalhost { get; set; } = false;
}
