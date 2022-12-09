namespace ThriveDevCenter.Client.Models;

public interface IDeletedResourceStatus
{
    /// <summary>
    ///   This is used to pretend that an item is deleted before we get the server re-fetch of data done
    /// </summary>
    public bool Deleted { get; set; }
}

public class DeletedResourceStatus : IDeletedResourceStatus
{
    public bool Deleted { get; set; }

    /// <summary>
    ///   Set to true when the delete is being processed
    /// </summary>
    public bool Processing { get; set; }
}
