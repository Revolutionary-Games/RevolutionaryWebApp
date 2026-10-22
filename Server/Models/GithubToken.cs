namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Utilities;

public class GithubToken : ModelWithCreationTime, IUpdateNotifications
{
    public const string TranslationsProtectionPurpose = "TranslationPublishingGitHubToken.v1";

    [Required]
    public GithubTokenPurpose Purpose { get; set; }

    [Required]
    [MaxLength(1024)]
    public string Ciphertext { get; set; } = string.Empty;

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new GithubTokenListUpdated
            {
                Type = entityState.ToChangeType(),
                Item = new GithubTokenDTO
                {
                    Id = Id,
                    Purpose = Purpose,
                    CreatedAt = CreatedAt,
                },
            },
            NotificationGroups.GithubTokenListUpdated);
    }
}
