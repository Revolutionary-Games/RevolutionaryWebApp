namespace ThriveDevCenter.Shared.Models
{
    public class StorageItemDTO : ClientSideTimedModel
    {
        public string Name { get; set; }
        public FileType Ftype { get; set; }
        public bool Special { get; set; }
        public int? Size { get; set; }
        public FileAccess ReadAccess { get; set; }
        public FileAccess WriteAccess { get; set; }
        public long? OwnerId { get; set; }
        public long? ParentId { get; set; }
        public bool AllowParentless { get; set; }
    }

    public class StorageItemInfo : ClientSideModel
    {
        public string Name { get; set; }
        public FileType Ftype { get; set; }
        public int? Size { get; set; }
        public FileAccess ReadAccess { get; set; }
    }
}
