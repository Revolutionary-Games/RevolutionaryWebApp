namespace ThriveDevCenter.Server.Models;

public interface IOwneableModel
{
    public long? OwnerId { get; set; }
    public User? Owner { get; set; }
}
