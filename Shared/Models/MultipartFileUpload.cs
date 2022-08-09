namespace ThriveDevCenter.Shared.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class MultipartFileUpload
{
    /// <summary>
    ///   The list of chunks to upload next. If null (can't be null in the initial request) means that all chunks
    ///   have already been returned.
    /// </summary>
    [MinLength(1)]
    public List<FileChunk>? NextChunks { get; set; }

    [Required]
    public string ChunkRetrieveToken { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int TotalChunks { get; set; }

    public class FileChunk
    {
        [Required]
        public string UploadURL { get; set; } = string.Empty;

        [Required]
        [Range(1, int.MaxValue)]
        public int ChunkNumber { get; set; }

        [Required]
        [Range(0, long.MaxValue)]
        public long Offset { get; set; }

        [Required]
        [Range(1, long.MaxValue)]
        public long Length { get; set; }
    }
}