namespace ThriveDevCenter.Shared.Models
{
    public class CIJobOutputSectionInfo
    {
        public long CiProjectId { get; set; }
        public long CiBuildId { get; set; }
        public long CiJobId { get; set; }
        public long CiJobOutputSectionId { get; set; }
        public string Name { get; set; }
        public CIJobSectionStatus Status { get; set; }
        public long OutputLength { get; set; }
    }
}
