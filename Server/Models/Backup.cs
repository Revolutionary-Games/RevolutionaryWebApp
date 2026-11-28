namespace RevolutionaryWebApp.Server.Models;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

// Retains metadata for backups created by older deployments.
[Index(nameof(Name), IsUnique = true)]
public class Backup : UpdateableModel
{
    public Backup(string name, long size)
    {
        Name = name;
        Size = size;
    }

    [Required]
    public string Name { get; set; }

    public long Size { get; set; }

    public bool Uploaded { get; set; }
}
