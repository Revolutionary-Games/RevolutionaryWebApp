namespace ThriveDevCenter.Server.Common.Models
{
    using System.ComponentModel.DataAnnotations;

    public class CiSecretExecutorData
    {
        [Required]
        public string SecretName { get; set; }

        public string SecretContent { get; set; }
    }
}
