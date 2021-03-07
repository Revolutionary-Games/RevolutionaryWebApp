namespace ThriveDevCenter.Shared.Models
{
    public class LFSProjectInfo : ClientSideTimedModel
    {
        public string Name { get; set; }

        public bool Public { get; set; }

        public long Size { get; set; }
    }
}
