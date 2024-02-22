namespace RevolutionaryWebApp.Server.Models;

using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Shared;

public abstract class BaseModel : IIdentifiable
{
    [Key]
    [AllowSortingBy]
    public long Id { get; set; }
}
