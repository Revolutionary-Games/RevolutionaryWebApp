namespace ThriveDevCenter.Server.Filters;

public class MyRateLimitOptions
{
    public int GlobalGetLimit { get; set; }
    public int GlobalPostLimit { get; set; }
    public int GlobalWindowSeconds { get; set; }
    public int SegmentsPerWindow { get; set; }

    public int QueueLimit { get; set; }

    public int LoginAndRegistrationLimit { get; set; }
    public int LoginWindowSeconds { get; set; }

    public int ShortWindowQueueLimit { get; set; }

    public int UserGlobalGetLimit { get; set; }
    public int UserGlobalPostLimit { get; set; }

    public bool AllowUnlimitedFromLocalhost { get; set; }
}
