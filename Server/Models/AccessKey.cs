namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Net;
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
        public DateTime? LastUsed { get; set; }

        public IPAddress LastUsedFrom { get; set; }

        [Required]
        [HashedLookUp]
        public string KeyCode { get; set; }

        public string HashedKeyCode { get; set; }

        [AllowSortingBy]
        public AccessKeyType KeyType { get; set; }

        public AccessKeyDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Description = Description,
                LastUsed = LastUsed,
                LastUsedFrom = LastUsedFrom,
                KeyType = KeyType
            };
        }
    }
}
