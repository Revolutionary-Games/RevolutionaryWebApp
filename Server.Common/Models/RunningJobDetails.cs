namespace RevolutionaryWebApp.Server.Common.Models;

using Shared.Models;

public class RunningJobDetails(CIJobDTO generalDetails)
{
    /// <summary>
    ///   General info
    /// </summary>
    public CIJobDTO GeneralDetails { get; set; } = generalDetails;

    // Special info properties only given to the active runner on what needs to be done to know
    public CiJobCacheConfiguration CacheConfiguration { get; set; } = new();
}
