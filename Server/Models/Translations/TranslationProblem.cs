namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;

[Index(nameof(ProjectId), nameof(Key), nameof(LanguageCode), nameof(Kind), IsUnique = true)]
[Index(nameof(ProjectId), nameof(LanguageCode), nameof(Ignored))]
public class TranslationProblem : IUpdateNotifications
{
    public long Id { get; set; }
    public long ProjectId { get; set; }

    [MaxLength(1000)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(35)]
    public string LanguageCode { get; set; } = string.Empty;

    public TranslationProblemKind Kind { get; set; }

    [MaxLength(20000)]
    public string Message { get; set; } = string.Empty;

    public bool Ignored { get; set; }
    public TranslationValue Value { get; set; } = null!;

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return TranslationNotificationHelper.ProblemsUpdated(ProjectId, Key, LanguageCode);
        yield return TranslationNotificationHelper.ProjectTranslationsUpdated(ProjectId);
    }
}
