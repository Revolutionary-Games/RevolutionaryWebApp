namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

    [Index(nameof(KeyCode), IsUnique = true)]
    public class AccessKey : UpdateableModel
    {
        [Required]
        public string Description { get; set; }

        public DateTime LastUsed { get; set; }

        [Required]
        public string KeyCode { get; set; }

        public int KeyType { get; set; }
    }
}
