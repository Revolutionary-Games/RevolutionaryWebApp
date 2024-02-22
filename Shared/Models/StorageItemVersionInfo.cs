namespace RevolutionaryWebApp.Shared.Models;

using DevCenterCommunication.Models;

public class StorageItemVersionInfo : ClientSideTimedModel
{
    public int Version { get; set; }
    public bool Keep { get; set; }
    public bool Protected { get; set; }
    public bool Uploading { get; set; }
    public long? Size { get; set; }
    public long? UploadedById { get; set; }
    public bool Deleted { get; set; }
}
