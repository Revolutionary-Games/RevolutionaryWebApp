namespace ThriveDevCenter.Shared.Forms
{
    using System.ComponentModel.DataAnnotations;
    using Models.Enums;

    public class CreateCISecretForm
    {
        [Required]
        [MaxLength(512)]
        [MinLength(1)]
        public string SecretName { get; set; } = string.Empty;

        [MaxLength(16000)]
        public string? SecretContent { get; set; }

        [Required]
        public CISecretType UsedForBuildTypes { get; set; }
    }
}
