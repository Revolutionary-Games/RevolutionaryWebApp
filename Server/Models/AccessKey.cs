namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Utilities;

    [Index(nameof(HashedKeyCode), IsUnique = true)]
    public class AccessKey : UpdateableModel, IContainsHashedLookUps
    {
        [Required]
        public string Description { get; set; }

        public DateTime LastUsed { get; set; }

        [Required]
        [HashedLookUp]
        public string KeyCode { get; set; }

        public string HashedKeyCode { get; set; }

        public int KeyType { get; set; }
    }
}
