namespace ThriveDevCenter.Server.Models
{
    using Shared.Models;

    public abstract class BaseModel : IIdentifiable
    {
        public long Id { get; set; }
    }
}
