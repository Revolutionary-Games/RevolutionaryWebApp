namespace ThriveDevCenter.Shared.Models
{
    public class CIJobOutputSectionDTO
    {
        public long CiProjectId { get; set; }
        public long CiBuildId { get; set; }
        public long CiJobId { get; set; }
        public long CiJobOutputSectionId { get; set; }
        public string Name { get; set; }
        public CIJobSectionStatus Status { get; set; }

        /// <summary>
        ///   This can contain megabytes of data
        /// </summary>
        public string Output { get; set; }

        public long OutputLength { get; set; }
    }
}
