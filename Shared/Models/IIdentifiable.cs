namespace ThriveDevCenter.Shared.Models;

public interface IIdentifiable
{
    public long Id { get; }
}

public abstract class ClientSideModel : IIdentifiable
{
    public long Id { get; set; }
}