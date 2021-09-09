namespace ThriveDevCenter.Shared.Models
{
    public class SentBulkEmailDTO : ClientSideModelWithCreationTime
    {
        public string Title { get; set; }
        public int Recipients { get; set; }
        public long? SentById { get; set; }
        public string SystemSend { get; set; }
    }
}
