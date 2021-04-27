namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class DevBuildsStatisticsDTO
    {
        public int TotalBuilds { get; set; }
        public int TotalDownloads { get; set; }
        public int DehydratedFiles { get; set; }
        public int ImportantBuilds { get; set; }
        public long TotalSpaceUsed { get; set; }
        public long DevBuildsSize { get; set; }
        public DateTime? BOTDUpdated { get; set; }
        public DateTime? LatestBuild { get; set; }
    }
}
