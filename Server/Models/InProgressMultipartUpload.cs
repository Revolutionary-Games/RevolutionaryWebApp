namespace ThriveDevCenter.Server.Models
{
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;

    [Index(nameof(UploadId))]
    public class InProgressMultipartUpload : UpdateableModel
    {
        [Required]
        public string UploadId { get; set; }

        [Required]
        public string Path { get; set; }

        public bool Finished { get; set; }

        public int NextChunkIndex { get; set; }
    }
}
