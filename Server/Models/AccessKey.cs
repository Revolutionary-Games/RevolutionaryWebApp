namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    using Shared;
    using Shared.Models;
    using Utilities;

    [Index(nameof(HashedKeyCode), IsUnique = true)]
    public class AccessKey : UpdateableModel, IContainsHashedLookUps
    {
        [Required]
        [AllowSortingBy]
        public string Description { get; set; }

        [AllowSortingBy]
        public DateTime LastUsed { get; set; }

        [Required]
        [HashedLookUp]
        public string KeyCode { get; set; }

        public string HashedKeyCode { get; set; }

        // TODO: change this into an enum
        [AllowSortingBy]
        public int KeyType { get; set; }

        public AccessKeyDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Description = Description,
                LastUsed = LastUsed,
                KeyType = KeyType
            };
        }
    }
}
