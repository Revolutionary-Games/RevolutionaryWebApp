namespace ThriveDevCenter.Shared.Models;

public class RepoReleaseStats
{
    public RepoReleaseStats(string repository)
    {
        Repository = repository;
    }

    public string Repository { get; set; }

    public string LatestRelease { get; set; } = "No latest release";
    public long LatestDownloads { get; set; }
    public long LatestLinuxDownloads { get; set; }
    public long LatestWindowsDownloads { get; set; }
    public long LatestMacDownloads { get; set; }

    public int TotalReleases { get; set; }
    public long TotalDownloads { get; set; }
    public long TotalLinuxDownloads { get; set; }
    public long TotalWindowsDownloads { get; set; }
    public long TotalMacDownloads { get; set; }
}
