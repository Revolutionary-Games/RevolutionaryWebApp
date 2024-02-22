namespace RevolutionaryWebApp.Shared;

using System;

public static class SiteNoticeTypeExtensions
{
    public static string AlertClass(this SiteNoticeType type)
    {
        switch (type)
        {
            case SiteNoticeType.Primary:
                return "alert-primary";
            case SiteNoticeType.Secondary:
                return "alert-secondary";
            case SiteNoticeType.Success:
                return "alert-success";
            case SiteNoticeType.Danger:
                return "alert-danger";
            case SiteNoticeType.Warning:
                return "alert-warning";
            case SiteNoticeType.Info:
                return "alert-info";
            case SiteNoticeType.Light:
                return "alert-light";
            case SiteNoticeType.Dark:
                return "alert-dark";
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }
}
