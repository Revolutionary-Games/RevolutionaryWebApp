namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Services;
using Shared.Models;
using SharedBase.Utilities;

/// <summary>
///   Imports current translations from the configured public repository branch.
/// </summary>
[DisableConcurrentExecution(600)]
public class ImportTranslationProjectJob
{
    private readonly ILogger<ImportTranslationProjectJob> logger;
    private readonly ApplicationDbContext database;
    private readonly ILocalTempFileLocks fileLocks;
    private readonly IBackgroundJobClient jobs;

    public ImportTranslationProjectJob(ILogger<ImportTranslationProjectJob> logger, ApplicationDbContext database,
        ILocalTempFileLocks fileLocks, IBackgroundJobClient jobs)
    {
        this.logger = logger;
        this.database = database;
        this.fileLocks = fileLocks;
        this.jobs = jobs;
    }

    public async Task Execute(long projectId, CancellationToken cancellationToken)
    {
        var project = await database.TranslationProjects.FindAsync([projectId], cancellationToken);
        if (project == null || !project.Enabled || string.IsNullOrEmpty(project.RepositoryFullName))
            return;

        ValidateConfiguration(project);
        var importConfiguration = (project.RepositoryFullName, project.SourceBranch, project.SourceFilePattern,
            project.TranslationFilePattern, project.SourceLanguage, project.Format);

        var checkout = fileLocks.GetTempFilePath($"translationRepos/{project.Id}");
        using (await fileLocks.LockAsync(checkout, cancellationToken))
        {
            var run = new TranslationSyncRun { ProjectId = projectId, Type = "Import", Status = "Running" };
            await database.TranslationSyncRuns.AddAsync(run, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);

            try
            {
                var url = $"https://github.com/{project.RepositoryFullName}.git";
                await GitRunHelpers.EnsureRepoIsCloned(url, checkout, true, cancellationToken);
                await GitRunHelpers.FetchRef(checkout, $"refs/heads/{project.SourceBranch}", cancellationToken);
                await GitRunHelpers.Checkout(checkout, $"origin/{project.SourceBranch}", true,
                    cancellationToken, true);
                await GitRunHelpers.Clean(checkout, cancellationToken);
                var sha = await GitRunHelpers.GetCurrentCommit(checkout, cancellationToken);

                await using var projectLock = sha != project.LastImportedSha ?
                    await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId, cancellationToken) :
                    null;
                if (projectLock != null)
                {
                    await database.Entry(project).ReloadAsync(cancellationToken);
                    if (database.Entry(project).State == EntityState.Detached)
                        throw new InvalidOperationException("Translation project was removed during import");

                    if (!project.Enabled || project.RepositoryFullName != importConfiguration.RepositoryFullName ||
                        project.SourceBranch != importConfiguration.SourceBranch ||
                        project.SourceFilePattern != importConfiguration.SourceFilePattern ||
                        project.TranslationFilePattern != importConfiguration.TranslationFilePattern ||
                        project.SourceLanguage != importConfiguration.SourceLanguage ||
                        project.Format != importConfiguration.Format)
                    {
                        throw new InvalidOperationException(
                            "Translation project configuration changed during import; retry the import");
                    }

                    if (sha != project.LastImportedSha)
                    {
                        await ImportFiles(project, checkout, sha, cancellationToken);
                        project.LastImportedSha = sha;
                        project.LastImportedAt = DateTime.UtcNow;
                    }
                }

                run.UpstreamSha = sha;
                run.Status = "Succeeded";
                run.CompletedAt = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Translation import failed for project {ProjectId}", projectId);
                database.ChangeTracker.Clear();
                run = await database.TranslationSyncRuns.FindAsync([run.Id], cancellationToken) ?? run;
                run.Status = "Failed";
                run.Error = exception.Message;
                run.CompletedAt = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                throw;
            }
        }
    }

    private static void ValidateConfiguration(TranslationProject project)
    {
        var parts = project.RepositoryFullName?.Split('/');
        if (parts is not { Length: 2 } || parts.Any(part => part.Length == 0 ||
                part.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_' and not '.')) ||
            string.IsNullOrWhiteSpace(project.SourceBranch) ||
            project.SourceBranch.Contains("..", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(project.SourceFilePattern) ||
            string.IsNullOrWhiteSpace(project.TranslationFilePattern) ||
            !project.TranslationFilePattern.Contains("{language}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid translation repository configuration");
        }
    }

    private async Task ImportFiles(TranslationProject project, string checkout, string sha,
        CancellationToken cancellationToken)
    {
        var languages = await database.TranslationProjectLanguages
            .Where(l => l.ProjectId == project.Id && l.Enabled && l.LanguageCode != project.SourceLanguage)
            .ToListAsync(cancellationToken);
        foreach (var discovered in DiscoverLanguages(checkout, project.TranslationFilePattern!,
                     project.SourceLanguage))
        {
            if (languages.Any(l => l.LanguageCode == discovered))
                continue;

            var language = new TranslationProjectLanguage
            {
                ProjectId = project.Id, LanguageCode = discovered, DisplayName = discovered,
            };
            await database.TranslationProjectLanguages.AddAsync(language, cancellationToken);
            languages.Add(language);
        }

        if (languages.Count == 0)
            throw new InvalidOperationException("No translation language files matched the configured pattern");

        var sourcePath = ResolveFile(checkout, project.SourceFilePattern!);
        var sourceContent = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        var englishPath = ResolveFile(checkout,
            project.TranslationFilePattern!.Replace("{language}", project.SourceLanguage, StringComparison.Ordinal));
        var englishContent = project.Format == TranslationFormat.Gettext ?
            await File.ReadAllTextAsync(englishPath, cancellationToken) :
            sourceContent;

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var newKeys = new HashSet<string>(StringComparer.Ordinal);
        var conflictWarnings = new List<string>();

        foreach (var language in languages)
        {
            var localePath = ResolveFile(checkout, language.FilePath ??
                project.TranslationFilePattern.Replace("{language}", language.LanguageCode,
                    StringComparison.Ordinal));
            if (!File.Exists(localePath))
                throw new FileNotFoundException("Configured translation file is missing", localePath);

            var localeContent = await File.ReadAllTextAsync(localePath, cancellationToken);
            var parsed = project.Format switch
            {
                TranslationFormat.Gettext => GettextTranslationParser.Parse(sourceContent, englishContent,
                    localeContent).Select(e => new ImportedEntry(e.Key, e.SourceText, e.TargetText,
                    e.Description, string.Join('\n', e.SourceLocations), e.Fuzzy)),
                TranslationFormat.Resx => ResxTranslationParser.Parse(sourceContent, localeContent)
                    .Select(e => new ImportedEntry(e.Key, e.SourceText, e.TargetText, e.Description,
                        null, false)),
                _ => throw new NotSupportedException("Manual projects do not import repository files yet"),
            };

            language.LastImportedAt = DateTime.UtcNow;

            foreach (var entry in parsed)
            {
                seenKeys.Add(entry.Key);
                var unit = await database.TranslationUnits.FindAsync([project.Id, entry.Key],
                    cancellationToken);
                var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.SourceText)));

                if (unit == null)
                {
                    newKeys.Add(entry.Key);
                    unit = new TranslationUnit
                    {
                        ProjectId = project.Id, Key = entry.Key, SourceText = entry.SourceText,
                        SourceHash = sourceHash, SourceRevision = 1, Description = entry.Description,
                        SourceLocations = entry.SourceLocations,
                    };
                    await database.TranslationUnits.AddAsync(unit, cancellationToken);
                }
                else if (unit.SourceHash != sourceHash)
                {
                    unit.SourceText = entry.SourceText;
                    unit.SourceHash = sourceHash;
                    ++unit.SourceRevision;
                    unit.SourceUpdatedAt = DateTime.UtcNow;
                    var oldValues = await database.TranslationValues
                        .Where(v => v.ProjectId == project.Id && v.Key == entry.Key).ToListAsync(cancellationToken);
                    foreach (var oldValue in oldValues)
                    {
                        if (oldValue.State == TranslationState.NeedsTranslation)
                            continue;

                        var oldState = oldValue.State;
                        oldValue.State = TranslationState.NeedsTranslation;
                        oldValue.Version = Guid.NewGuid();
                        oldValue.PendingSince ??= DateTime.UtcNow;
                        oldValue.PendingRevision = ++project.CurrentRevision;
                        await database.TranslationChanges.AddAsync(new TranslationChange
                        {
                            ProjectId = project.Id, Key = entry.Key, LanguageCode = oldValue.LanguageCode,
                            Kind = TranslationChangeKind.SourceChanged, OldText = oldValue.Text,
                            NewText = oldValue.Text, OldState = oldState, NewState = oldValue.State,
                            SourceRevision = unit.SourceRevision, ProjectRevision = project.CurrentRevision,
                        }, cancellationToken);
                    }
                }

                unit.Active = true;
                unit.Description = entry.Description;
                unit.SourceLocations = entry.SourceLocations;

                var value = await database.TranslationValues.FindAsync([project.Id, entry.Key, language.LanguageCode],
                    cancellationToken);
                if (value == null)
                {
                    await database.TranslationValues.AddAsync(new TranslationValue
                    {
                        ProjectId = project.Id, Key = entry.Key, LanguageCode = language.LanguageCode,
                        Text = entry.TargetText, LastImportedText = entry.TargetText,
                        State = entry.Fuzzy || string.IsNullOrWhiteSpace(entry.TargetText) ?
                            TranslationState.NeedsTranslation :
                            TranslationState.Translated,
                        SourceRevisionTranslatedFrom = unit.SourceRevision,
                        SourceTextTranslatedFrom = entry.SourceText,
                    }, cancellationToken);
                    continue;
                }

                var previousEditor = value.LastEditedByUserId;
                var previousText = value.Text;
                var previousState = value.State;
                var result = TranslationRemoteImport.Apply(value, entry.TargetText, entry.Fuzzy);
                if (result == TranslationRemoteImportResult.Conflict)
                {
                    var exists = await database.TranslationImportConflicts.AnyAsync(c => c.ProjectId == project.Id &&
                        c.Key == entry.Key && c.LanguageCode == language.LanguageCode && c.ResolvedAt == null &&
                        c.IncomingText == entry.TargetText, cancellationToken);
                    if (!exists)
                    {
                        await database.TranslationImportConflicts.AddAsync(new TranslationImportConflict
                        {
                            ProjectId = project.Id, Key = entry.Key, LanguageCode = language.LanguageCode,
                            LocalText = value.Text, IncomingText = entry.TargetText,
                        }, cancellationToken);
                        conflictWarnings.Add($"Translation conflict in {project.Name}, " +
                            $"{language.LanguageCode}, key {entry.Key}. " +
                            "An admin needs to resolve it in the translation UI.");
                    }
                }
                else if (result == TranslationRemoteImportResult.TextChanged)
                {
                    await database.TranslationChanges.AddAsync(new TranslationChange
                    {
                        ProjectId = project.Id, Key = entry.Key, LanguageCode = language.LanguageCode,
                        Kind = TranslationChangeKind.SystemImport, OldText = previousText,
                        NewText = value.Text, OldState = previousState, NewState = value.State,
                        SourceRevision = unit.SourceRevision, ProjectRevision = project.CurrentRevision,
                    }, cancellationToken);

                    if (previousEditor != null)
                    {
                        var notificationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                            $"remote:{project.Id}:{entry.Key}:{language.LanguageCode}:{sha}")));
                        await database.UserNotifications.AddAsync(new UserNotification
                        {
                            UserId = previousEditor.Value, DeduplicationKey = notificationKey,
                            Type = UserNotificationType.TranslationChangedInRepository,
                            Message = $"Your {language.DisplayName} translation for {entry.Key} changed in Git.",
                            TargetPath = $"/translations/{project.Slug}",
                        }, cancellationToken);
                    }

                    var subscribers = await database.TranslationSubscriptions.AsNoTracking().Where(s =>
                            s.ProjectId == project.Id && s.LanguageCode == language.LanguageCode &&
                            s.EventType == TranslationSubscriptionEventType.TranslationUpdate &&
                            s.UserId != previousEditor)
                        .Select(s => s.UserId).ToListAsync(cancellationToken);
                    foreach (var subscriber in subscribers)
                    {
                        var notificationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                            $"remote-update:{project.Id}:{entry.Key}:{language.LanguageCode}:{sha}")));
                        await database.UserNotifications.AddAsync(new UserNotification
                        {
                            UserId = subscriber, DeduplicationKey = notificationKey,
                            Type = UserNotificationType.TranslationUpdated,
                            Message = $"A {language.DisplayName} translation for {entry.Key} changed in Git.",
                            TargetPath = $"/translations/{project.Slug}/{language.LanguageCode}/edit" +
                                $"?key={Uri.EscapeDataString(entry.Key)}",
                        }, cancellationToken);
                    }
                }
            }
        }

        var vanished = await database.TranslationUnits.Where(u => u.ProjectId == project.Id && u.Active &&
            !seenKeys.Contains(u.Key)).ToListAsync(cancellationToken);
        foreach (var unit in vanished)
            unit.Active = false;

        if (project.LastImportedSha != null && newKeys.Count > 0)
        {
            var subscribers = await database.TranslationSubscriptions.AsNoTracking()
                .Where(s => s.ProjectId == project.Id && s.EventType == TranslationSubscriptionEventType.NewKey)
                .ToListAsync(cancellationToken);
            foreach (var subscriber in subscribers)
            {
                foreach (var key in newKeys)
                {
                    var notificationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"new-key:{project.Id}:{key}:{subscriber.LanguageCode}:{sha}")));
                    await database.UserNotifications.AddAsync(new UserNotification
                    {
                        UserId = subscriber.UserId, DeduplicationKey = notificationKey,
                        Type = UserNotificationType.TranslationKeyAdded,
                        Message = $"New {subscriber.LanguageCode} translation key: {key}",
                        TargetPath = $"/translations/{project.Slug}/{subscriber.LanguageCode}/edit" +
                            $"?key={Uri.EscapeDataString(key)}",
                    }, cancellationToken);
                }
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        foreach (var warning in conflictWarnings)
        {
            try
            {
                jobs.Enqueue<SendDiscordWebhookMessageJob>(job => job.Execute("TranslationConflict",
                    warning, CancellationToken.None));
            }
            catch (Exception error)
            {
                logger.LogError(error, "Could not queue translation conflict warning after import commit");
            }
        }
    }

    private string ResolveFile(string checkout, string relativePath)
    {
        var root = Path.GetFullPath(checkout) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!result.StartsWith(root, StringComparison.Ordinal) ||
            relativePath.Contains('*', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Translation file path must be a path inside the checkout");
        }

        // A tracked symlink can point outside the checkout even when its lexical path is safe.
        var current = Path.GetFullPath(checkout);
        foreach (var segment in Path.GetRelativePath(current, result).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Translation files may not pass through symlinks");
            }
        }

        return result;
    }

    private IEnumerable<string> DiscoverLanguages(string checkout, string pattern, string sourceLanguage)
    {
        const string placeholder = "{language}";
        var relativeDirectory = Path.GetDirectoryName(pattern) ?? string.Empty;
        var filePattern = Path.GetFileName(pattern);
        var marker = filePattern.IndexOf(placeholder, StringComparison.Ordinal);
        if (marker < 0)
            throw new InvalidOperationException("Language file pattern must contain {language} in the filename");

        var prefix = filePattern[..marker];
        var suffix = filePattern[(marker + placeholder.Length)..];
        var directory = ResolveFile(checkout, Path.Combine(relativeDirectory, "placeholder"));
        directory = Path.GetDirectoryName(directory)!;
        if (!Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) &&
                name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > prefix.Length + suffix.Length)
            .Select(name => name[prefix.Length..^suffix.Length])
            .Where(language => language != sourceLanguage && language.Length <= 35 &&
                language.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'));
    }

    private sealed record ImportedEntry(string Key, string SourceText, string TargetText,
        string? Description, string? SourceLocations, bool Fuzzy);
}
