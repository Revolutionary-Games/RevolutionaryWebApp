namespace ThriveDevCenter.Server.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Utilities;

// TODO: drop the update info from this table as unnecessary in a later migration
[Index(nameof(Sha3), IsUnique = true)]
[Index(nameof(StorageItemId))]
public class DehydratedObject : UpdateableModel
{
    [Required]
    public string Sha3 { get; set; } = string.Empty;

    public long StorageItemId { get; set; }

    public StorageItem? StorageItem { get; set; }

    /// <summary>
    ///   DevBuilds that contain this object
    /// </summary>
    public ICollection<DevBuild> DevBuilds { get; set; } = new HashSet<DevBuild>();

    public async Task<bool> IsUploaded(ApplicationDbContext database)
    {
        if (StorageItem == null)
            throw new NotLoadedModelNavigationException();

        var version = await StorageItem.GetHighestVersion(database);

        if (version == null)
            return false;

        return !version.Uploading;
    }
}
