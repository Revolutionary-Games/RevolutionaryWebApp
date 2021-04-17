namespace ThriveDevCenter.Shared.Models
{
    using System;

    public class SoftDeletedResource : ClientSideModel
    {
        public string Name { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
