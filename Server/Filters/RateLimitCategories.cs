namespace RevolutionaryWebApp.Server.Filters;

public static class RateLimitCategories
{
    public const string LoginLimit = "login";
    public const string RegistrationLimit = "registration";
    public const string CodeRedeemLimit = "codeRedeem";
    public const string EmailVerification = "email";
    public const string CrashReport = "crash";
    public const string Stackwalk = "stackwalk";
}
