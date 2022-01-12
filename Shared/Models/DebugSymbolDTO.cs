namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class DebugSymbolDTO : ClientSideTimedModel, ICloneable
    {
        public string Name { get; set; }
        public string RelativePath { get; set; }
        public bool Active { get; set; }
        public bool Uploaded { get; set; }
        public long Size { get; set; }
        public long StoredInItemId { get; set; }
        public long? CreatedById { get; set; }

        public object Clone()
        {
            return new DebugSymbolDTO()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Name = Name,
                RelativePath = RelativePath,
                Active = Active,
                Uploaded = Uploaded,
                Size = Size,
                StoredInItemId = StoredInItemId,
                CreatedById = CreatedById,
            };
        }
    }
}
