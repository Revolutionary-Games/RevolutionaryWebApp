namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using Microsoft.EntityFrameworkCore;
    using Utilities;

    /// <summary>
    ///   Represents a stackwalk task that needs to be performed
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     For now this is just used for the stackwalk tool but in the future if we have an async API to the stackwalk
    ///     service we could also use this for the crash report stackwalking
    ///   </para>
    /// </remarks>
    [Index(nameof(HashedId), IsUnique = true)]
    public class StackwalkTask : IContainsHashedLookUps
    {
        public const string CrashDumpToolTempStorageFolderName = "stackwalkToolRequests";

        [Key]
        [HashedLookUp]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string HashedId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? FinishedAt { get; set; }

        [Required]
        public string DumpTempCategory { get; set; }

        [Required]
        public string DumpFileName { get; set; }

        public bool DeleteDumpAfterRunning { get; set; } = true;

        /// <summary>
        ///   Will be used in the future once crash reports are processed through this
        /// </summary>
        public string SpecialWalkType { get; set; }

        public string Result { get; set; }

        public bool Succeeded { get; set; }

        [NotMapped]
        public bool FinishedSuccessfully => FinishedAt != null && Succeeded;
    }
}
