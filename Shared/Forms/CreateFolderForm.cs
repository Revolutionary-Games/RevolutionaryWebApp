namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using Models;

    public class CreateFolderForm
    {
        [Required]
        [StringLength(120, MinimumLength = 3)]
        public string Name { get; set; } = string.Empty;

        public long? ParentFolder { get; set; }

        [Required]
        public FileAccess ReadAccess { get; set; }

        [Required]
        public FileAccess WriteAccess { get; set; }
    }
}
