namespace ThriveDevCenter.Shared.Models
{
    public class StorageItemVersionInfo : ClientSideTimedModel
    {
        public int Version { get; set; }
        public bool Keep { get; set; }
        public bool Protected { get; set; }
        public bool Uploading { get; set; }
        public int? Size { get; set; }
    }
}
