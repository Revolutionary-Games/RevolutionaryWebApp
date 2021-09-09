namespace ThriveDevCenter.Shared.Models
{
    using System;

    public abstract class ClientSideModelWithCreationTime : ClientSideModel
    {
        public DateTime CreatedAt { get; set; }
    }
}
