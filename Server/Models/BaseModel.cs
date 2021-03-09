namespace ThriveDevCenter.Server.Models
{
    using Shared;
    using Shared.Models;

    public abstract class BaseModel : IIdentifiable
    {
        [AllowSortingBy]
        public long Id { get; set; }
    }
}
