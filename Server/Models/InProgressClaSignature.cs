namespace ThriveDevCenter.Server.Models
{
    using System;
    using System.Collections.Generic;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;
    using Shared.Notifications;

    /// <summary>
    ///   Holds CLA signing data while the user is working on it. Associated with a session
    /// </summary>
    [Index(nameof(SessionId), IsUnique = true)]
    [Index(nameof(EmailVerificationCode), IsUnique = true)]
    public class InProgressClaSignature : UpdateableModel, IUpdateNotifications
    {
        public Guid SessionId { get; set; }

        public long ClaId { get; set; }

        public string Email { get; set; }

        public bool EmailVerified { get; set; }

        public Guid? EmailVerificationCode { get; set; }

        public string GithubAccount { get; set; }

        public bool GithubSkipped { get; set; }

        public string DeveloperUsername { get; set; }

        public string SignerName { get; set; }

        public string SignerSignature { get; set; }

        public bool? SignerIsMinor { get; set; }

        public string GuardianName { get; set; }

        public string GuardianSignature { get; set; }

        public Session Session { get; set; }

        public Cla Cla { get; set; }

        public InProgressClaSignatureDTO GetDTO()
        {
            return new()
            {
                Id = Id,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                ClaId = ClaId,
                Email = Email,
                EmailVerified = EmailVerified,
                GithubAccount = GithubAccount,
                GithubSkipped = GithubSkipped,
                DeveloperUsername = DeveloperUsername,
                SignerName = SignerName,
                SignerSignature = SignerSignature,
                SignerIsMinor = SignerIsMinor,
                GuardianName = GuardianName,
                GuardianSignature = GuardianSignature,
            };
        }

        public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
        {
            yield return new Tuple<SerializedNotification, string>(
                new InProgressClaSignatureUpdated() { Item = GetDTO() },
                NotificationGroups.InProgressCLASignatureUpdated + SessionId);
        }
    }
}
