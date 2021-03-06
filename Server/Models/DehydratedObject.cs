namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

    // TODO: drop the update info from this table as unnecessary in a later migration
    [Index(nameof(sha3), IsUnique=true)]
    public class DehydratedObject : UpdateableModel
    {
        [Required]
        public string sha3 { get; set; }

        // storage_item_id bigint,
    }
}
