namespace RevolutionaryWebApp.Server.Common.Models;

using System.ComponentModel.DataAnnotations;

public class CiSecretExecutorData
{
    [Required]
    public string SecretName { get; set; } = string.Empty;

    public string SecretContent { get; set; } = string.Empty;
}
