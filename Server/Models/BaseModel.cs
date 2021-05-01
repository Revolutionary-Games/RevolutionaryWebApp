namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Shared;
    using Shared.Models;

    public abstract class BaseModel : IIdentifiable
    {
        [Key]
        [AllowSortingBy]
        public long Id { get; set; }
    }
}
