namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Services;
using Shared.Models;
using Utilities;

/// <summary>
///   Regenerates an app-owned PR branch from the latest public upstream branch.
/// </summary>
[DisableConcurrentExecution(600)]
public sealed class PublishTranslationProjectJob
{
    private readonly ApplicationDbContext database;
    private readonly TranslationRepositoryOperations repository;
    private readonly ILocalTempFileLocks fileLocks;
    private readonly ITranslationGithubTokenProvider tokenProvider;
    private readonly IConfiguration configuration;
    private readonly ILogger<PublishTranslationProjectJob> logger;
    private readonly IModelUpdateNotificationSender notificationSender;

    public PublishTranslationProjectJob(ApplicationDbContext database, ILocalTempFileLocks fileLocks,
        ITranslationGithubTokenProvider tokenProvider, IConfiguration configuration,
        ILogger<PublishTranslationProjectJob> logger, IModelUpdateNotificationSender notificationSender,
        TranslationRepositoryOperations? repository = null)
    {
        this.database = database;
        this.repository = repository ?? new TranslationRepositoryOperations();
        this.fileLocks = fileLocks;
        this.tokenProvider = tokenProvider;
        this.configuration = configuration;
        this.logger = logger;
        this.notificationSender = notificationSender;
    }

    public async Task Execute(long projectId, CancellationToken cancellationToken)
    {
        var project = await database.TranslationProjects.FindAsync([projectId], cancellationToken);
        if (project == null || project.Deleted || !project.Enabled || project.Format == TranslationFormat.Manual)
            return;

        var token = await tokenProvider.GetToken(cancellationToken) ??
            throw new InvalidOperationException("Translation publishing GitHub token is not configured");
        var checkout = fileLocks.GetTempFilePath($"translationRepos/{project.Id}");
        using (await fileLocks.LockAsync(checkout, cancellationToken))
        {
            await using var repositoryLock = await TranslationProjectAdvisoryLock.AcquireForRepositorySyncAsync(
                database, projectId, cancellationToken);
            await database.Entry(project).ReloadAsync(cancellationToken);
            if (database.Entry(project).State == EntityState.Detached || project.Deleted || !project.Enabled ||
                project.Format == TranslationFormat.Manual)
            {
                return;
            }

            var run = new TranslationSyncRun { ProjectId = project.Id, Type = "Publish", Status = "Running" };
            database.TranslationSyncRuns.Add(run);
            await database.SaveChangesAsync(cancellationToken);

            try
            {
                await Publish(project, checkout, token, run, cancellationToken);
                run.Status = "Succeeded";
                run.CompletedAt = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception error)
            {
                logger.LogError(error, "Translation publish failed for project {ProjectId}", projectId);
                database.ChangeTracker.Clear();
                run = await database.TranslationSyncRuns.FindAsync([run.Id], cancellationToken) ?? run;
                run.Status = "Failed";
                run.Error = error.Message;
                run.CompletedAt = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                throw;
            }
        }
    }

    private static string CreditName(User user)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
        return name.Replace('\r', ' ').Replace('\n', ' ').Replace('<', ' ').Replace('>', ' ');
    }

    private static User[] Contributors(IEnumerable<TranslationChange> changes,
        IReadOnlyDictionary<long, User> authors)
    {
        return changes.Where(change => change.CountsAsContribution && change.ContributorUserId != null)
            .GroupBy(change => change.ContributorUserId!.Value).OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key).Select(group => authors.GetValueOrDefault(group.Key))
            .OfType<User>().ToArray();
    }

    private static string SafeFile(string checkout, string relativePath)
    {
        var root = Path.GetFullPath(checkout) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root, StringComparison.Ordinal) || relativePath.Contains('*') ||
            relativePath.Contains('\\'))
        {
            throw new InvalidOperationException("Translation file path must remain inside the checkout");
        }

        var current = Path.GetFullPath(checkout);
        foreach (var part in Path.GetRelativePath(current, fullPath).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Translation files may not pass through symlinks");
            }
        }

        return fullPath;
    }

    private static IReadOnlyCollection<TranslationChange> UnmergedChanges(IReadOnlyCollection<TranslationValue> values,
        IReadOnlyCollection<TranslationChange> history,
        TranslationFormat format)
    {
        var byValue = history.ToLookup(change => (change.Key, change.LanguageCode));
        var changes = new List<TranslationChange>();
        foreach (var value in values)
        {
            // Every publish rebuilds the entire branch from upstream, including previously published work.
            // PendingRevision and the PR's LastPublishedRevision only describe delivery, not merging.
            bool MatchesUpstream(string text, TranslationState state)
            {
                return text == value.LastImportedText && (format != TranslationFormat.Gettext ||
                    state == TranslationState.NeedsTranslation == value.LastImportedFuzzy);
            }

            if (MatchesUpstream(value.Text, value.State))
                continue;

            var valueHistory = byValue[(value.Key, value.LanguageCode)]
                .OrderBy(change => change.ProjectRevision).ThenBy(change => change.Id).ToArray();

            // Exclude earlier contributions which have already reached upstream (or were reverted).
            var baseline = Array.FindLastIndex(valueHistory,
                change => MatchesUpstream(change.NewText, change.NewState));
            changes.AddRange(valueHistory.Skip(baseline + 1));
        }

        return changes;
    }

    private static void ValidateSnapshotConfiguration(TranslationProject project,
        TranslationProjectConfigurationDTO expected)
    {
        if (project.Deleted || !project.Enabled || project.RepositoryFullName != expected.RepositoryFullName ||
            project.SourceBranch != expected.SourceBranch || project.SourceFilePattern != expected.SourceFilePath ||
            project.TranslationFilePattern != expected.TranslationFilePattern ||
            project.SourceLanguage != expected.SourceLanguage || project.Format != expected.Format ||
            project.LastImportedSha != expected.LastImportedSha)
        {
            throw new InvalidOperationException(
                "Translation project configuration changed during publish; retry the publish");
        }
    }

    private async Task Publish(TranslationProject project, string checkout, string token,
        TranslationSyncRun run, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.RepositoryFullName) || string.IsNullOrEmpty(project.SourceBranch) ||
            string.IsNullOrEmpty(project.SourceFilePattern) || string.IsNullOrEmpty(project.TranslationFilePattern))
        {
            throw new InvalidOperationException("Translation repository configuration is incomplete");
        }

        var expectedConfiguration = project.GetConfigurationDTO();
        var upstreamSha = await repository.PrepareCheckout(project.RepositoryFullName, project.SourceBranch,
            checkout, cancellationToken);
        run.UpstreamSha = upstreamSha;

        List<TranslationValue> values;
        List<TranslationProjectLanguage> snapshotLanguages;
        List<TranslationChange> history;
        TranslationPullRequest? tracked;
        long snapshotRevision;
        await using (await TranslationProjectAdvisoryLock.AcquireForSyncAsync(database, project.Id,
                         notificationSender, cancellationToken))
        {
            await database.Entry(project).ReloadAsync(cancellationToken);
            ValidateSnapshotConfiguration(project, expectedConfiguration);
            if (upstreamSha != project.LastImportedSha)
            {
                throw new InvalidOperationException(
                    "Upstream changed since import; import the new commit before publishing");
            }

            if (await database.TranslationImportConflicts.AnyAsync(c => c.ProjectId == project.Id &&
                    c.ResolvedAt == null && c.Value.Language.Enabled, cancellationToken))
            {
                throw new InvalidOperationException("Resolve translation import conflicts before publishing");
            }

            snapshotLanguages = await database.TranslationProjectLanguages.AsNoTracking()
                .Where(language => language.ProjectId == project.Id && language.Enabled)
                .OrderBy(language => language.LanguageCode).ToListAsync(cancellationToken);
            var languageCodes = snapshotLanguages.Select(language => language.LanguageCode).ToArray();

            // The project and languages have been checked for soft deletion. Scope these bulk reads
            // explicitly to avoid expensive navigation filters. Detached values keep the snapshot stable.
            values = await database.TranslationValues.IgnoreQueryFilters().AsNoTracking()
                .Where(value => value.ProjectId == project.Id && languageCodes.Contains(value.LanguageCode))
                .Select(value => new TranslationValue
                {
                    ProjectId = value.ProjectId, Key = value.Key, LanguageCode = value.LanguageCode,
                    Text = value.Text, State = value.State, LastImportedText = value.LastImportedText,
                    LastImportedFuzzy = value.LastImportedFuzzy, PendingRevision = value.PendingRevision,
                }).ToListAsync(cancellationToken);
            snapshotRevision = project.CurrentRevision;
            history = await database.TranslationChanges.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.ProjectId == project.Id && c.ProjectRevision <= snapshotRevision &&
                    languageCodes.Contains(c.LanguageCode) && (c.Value.Text != c.Value.LastImportedText ||
                        (project.Format == TranslationFormat.Gettext &&
                            (c.Value.State == TranslationState.NeedsTranslation) != c.Value.LastImportedFuzzy)))
                .ToListAsync(cancellationToken);
            tracked = await database.TranslationPullRequests.AsNoTracking()
                .FirstOrDefaultAsync(pr => pr.ProjectId == project.Id, cancellationToken);
        }

        var pending = values.Where(v => v.PendingRevision != null).ToArray();
        if (pending.Length == 0 && tracked is not { Open: true })
            return;

        using var github = repository.CreateGithubClient(token);
        var forkOwner = await github.GetLogin(cancellationToken);
        await github.EnsureFork(project.RepositoryFullName, forkOwner, cancellationToken);
        TranslationGithubPullRequest? remotePr = null;
        if (tracked is { Open: true })
        {
            // TODO: we should probably trigger a github comment for these problems to make them visible
            remotePr = await github.GetPullRequest(project.RepositoryFullName, tracked.Number, cancellationToken);
            if (remotePr is { Open: true } && (remotePr.HeadBranch != tracked.HeadBranch ||
                    remotePr.HeadOwner != forkOwner || remotePr.BaseBranch != project.SourceBranch))
            {
                throw new InvalidOperationException("Tracked translation PR points to an unexpected branch");
            }

            if (remotePr is { Open: true } && remotePr.HeadSha != tracked.HeadSha)
                throw new InvalidOperationException("The translation PR branch changed outside this app");
        }

        var reusePr = remotePr is { Open: true };
        var branch = reusePr ?
            tracked!.HeadBranch :
            $"devcenter/translations/{project.Id}/{DateTime.UtcNow:yyyyMMddHHmmss}";
        await repository.CheckoutNewBranch(checkout, branch, project.SourceBranch, cancellationToken);

        var sourceContent = await File.ReadAllTextAsync(SafeFile(checkout, project.SourceFilePattern),
            cancellationToken);
        var changes = UnmergedChanges(values, history, project.Format);
        var authors = await database.Users.AsNoTracking().Where(u =>
            changes.Select(c => c.ContributorUserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id,
            cancellationToken);
        var commitCount = 0;
        var automatedFiles = new List<string>();

        var valuesByLanguage = values.ToLookup(value => value.LanguageCode);
        var languages = snapshotLanguages.Where(language => language.LanguageCode != project.SourceLanguage);
        var sourceValues = valuesByLanguage[project.SourceLanguage].ToArray();
        var sourcePath = project.Format == TranslationFormat.Gettext ?
            project.TranslationFilePattern!.Replace("{language}", project.SourceLanguage, StringComparison.Ordinal) :
            project.SourceFilePattern!;
        var sourceFullPath = SafeFile(checkout, sourcePath);
        var sourceOriginal = await File.ReadAllTextAsync(sourceFullPath, cancellationToken);
        var sourceOutput = project.Format == TranslationFormat.Gettext ?
            TranslationFileExporter.ExportGettext(sourceContent, sourceOriginal, sourceValues,
                changes.Where(change => change.LanguageCode == project.SourceLanguage)
                    .Max(change => (DateTime?)change.CreatedAt),
                LastTranslator(project.SourceLanguage, changes, authors)) :
            TranslationFileExporter.ExportResx(sourceContent, sourceContent, sourceValues);

        if (project.Format == TranslationFormat.Gettext)
        {
            var parsedSource = GettextTranslationParser.Parse(sourceContent, sourceOutput, sourceOutput)
                .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
            foreach (var value in sourceValues.Where(value => parsedSource.ContainsKey(value.Key)))
            {
                if (parsedSource[value.Key].TargetText != value.Text)
                    throw new InvalidOperationException($"Gettext export changed the source value of {value.Key}");
            }
        }
        else if (project.Format == TranslationFormat.Resx)
        {
            var parsedSource = ResxTranslationParser.Parse(sourceOutput, sourceOutput)
                .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
            foreach (var value in sourceValues.Where(value => parsedSource.ContainsKey(value.Key)))
            {
                if (parsedSource[value.Key].SourceText != value.Text)
                    throw new InvalidOperationException($"RESX export changed the source value of {value.Key}");
            }

            sourceContent = sourceOutput;
        }
        else
        {
            throw new NotImplementedException("Unimplemented project format: {project.Format}");
        }

        if (await WriteAndCommitFile(checkout, sourcePath, project.SourceLanguage, sourceOriginal, sourceOutput,
                changes, authors, automatedFiles, cancellationToken))
        {
            ++commitCount;
        }

        foreach (var language in languages)
        {
            var path = language.FilePath ?? project.TranslationFilePattern.Replace("{language}",
                language.LanguageCode, StringComparison.Ordinal);
            var fullPath = SafeFile(checkout, path);
            var original = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var localeValues = valuesByLanguage[language.LanguageCode].ToArray();
            var content = project.Format switch
            {
                TranslationFormat.Gettext => TranslationFileExporter.ExportGettext(sourceContent,
                    original, localeValues, changes.Where(change => change.LanguageCode == language.LanguageCode)
                        .Max(change => (DateTime?)change.CreatedAt),
                    LastTranslator(language.LanguageCode, changes, authors)),
                TranslationFormat.Resx => TranslationFileExporter.ExportResx(sourceContent,
                    original, localeValues),
                _ => throw new NotSupportedException("Manual projects do not publish to GitHub"),
            };
            if (content == original)
                continue;

            if (project.Format == TranslationFormat.Gettext)
            {
                var parsed = GettextTranslationParser.Parse(sourceContent, sourceContent, content)
                    .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
                foreach (var value in localeValues.Where(value => parsed.ContainsKey(value.Key)))
                {
                    if (parsed[value.Key].TargetText != value.Text)
                        throw new InvalidOperationException($"Gettext export changed the value of {value.Key}");
                }
            }
            else
            {
                var parsed = ResxTranslationParser.Parse(sourceContent, content)
                    .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
                foreach (var value in localeValues.Where(value => parsed.ContainsKey(value.Key)))
                {
                    if (parsed[value.Key].TargetText != value.Text)
                        throw new InvalidOperationException($"RESX export changed the value of {value.Key}");
                }
            }

            if (await WriteAndCommitFile(checkout, path, language.LanguageCode, original, content, changes, authors,
                    automatedFiles, cancellationToken))
            {
                ++commitCount;
            }
        }

        if (automatedFiles.Count > 0)
        {
            await repository.CommitFile(checkout, ".", "Update automated translation states",
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_NAME"] = "DevCenter Translations",
                    ["GIT_AUTHOR_EMAIL"] = BotEmail(),
                    ["GIT_COMMITTER_NAME"] = "DevCenter Translations",
                    ["GIT_COMMITTER_EMAIL"] = BotEmail(),
                }, cancellationToken);
            ++commitCount;
        }

        // Recheck configuration before delivery and save bookkeeping under the editing lock. File
        // generation, commits and fork preparation above do not need to block translators.
        await using var deliveryLock = await TranslationProjectAdvisoryLock.AcquireForSyncAsync(database, project.Id,
            notificationSender, cancellationToken);
        await database.Entry(project).ReloadAsync(cancellationToken);
        ValidateSnapshotConfiguration(project, expectedConfiguration);
        var currentLanguages = await database.TranslationProjectLanguages.AsNoTracking()
            .Where(language => language.ProjectId == project.Id && language.Enabled)
            .ToDictionaryAsync(language => language.LanguageCode, language => language.FilePath, cancellationToken);
        if (currentLanguages.Count != snapshotLanguages.Count || snapshotLanguages.Any(language =>
                !currentLanguages.TryGetValue(language.LanguageCode, out var path) || path != language.FilePath))
        {
            throw new InvalidOperationException(
                "Translation language configuration changed during publish; retry the publish");
        }

        if (commitCount == 0)
        {
            await CompletePublish(project.Id, pending, snapshotRevision, cancellationToken);
            return;
        }

        var headSha = await repository.GetCurrentCommit(checkout, cancellationToken);
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
        var authEnvironment = new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {auth}",
        };
        var expected = reusePr ? tracked!.HeadSha ?? string.Empty : string.Empty;
        await repository.PushBranch(checkout,
            $"https://github.com/{forkOwner}/{project.RepositoryFullName.Split('/')[1]}.git", branch, expected,
            authEnvironment, cancellationToken);

        var link = new Uri(configuration.GetBaseUrl(), "translations/" + Uri.EscapeDataString(project.Slug)).ToString();

        var pr = reusePr ?
            remotePr! :
            await github.CreatePullRequest(project.RepositoryFullName,
                project.SourceBranch, forkOwner, branch, link, cancellationToken);
        tracked = await database.TranslationPullRequests.FindAsync([project.Id], cancellationToken) ??
            new TranslationPullRequest { ProjectId = project.Id };
        if (database.Entry(tracked).State == EntityState.Detached)
            database.TranslationPullRequests.Add(tracked);
        tracked.Number = pr.Number;
        tracked.HeadBranch = branch;
        tracked.HeadSha = headSha;
        tracked.Open = true;
        tracked.LastCheckedAt = DateTime.UtcNow;
        tracked.LastPublishedRevision = snapshotRevision;
        await CompletePublish(project.Id, pending, snapshotRevision, cancellationToken);
    }

    private async Task CompletePublish(long projectId, IReadOnlyCollection<TranslationValue> pending,
        long revision, CancellationToken cancellationToken)
    {
        // Read the current pending revisions in bulk. A newer edit must remain pending even if this
        // push delivered an earlier version of the same translation.
        var delivered = pending.ToDictionary(value => (value.Key, value.LanguageCode));
        var current = await database.TranslationValues.IgnoreQueryFilters().AsNoTracking()
            .Where(value => value.ProjectId == projectId && value.PendingRevision != null)
            .ToListAsync(cancellationToken);
        var trackedValues = database.ChangeTracker.Entries<TranslationValue>()
            .ToDictionary(entry => (entry.Entity.ProjectId, entry.Entity.Key, entry.Entity.LanguageCode));
        foreach (var value in current)
        {
            if (!delivered.TryGetValue((value.Key, value.LanguageCode), out var snapshot))
                continue;

            if (trackedValues.TryGetValue((projectId, value.Key, value.LanguageCode), out var entry))
            {
                entry.CurrentValues.SetValues(value);
                entry.OriginalValues.SetValues(value);
            }
            else
            {
                entry = database.TranslationValues.Attach(value);
            }

            entry.Entity.LastPublishedRevision = snapshot.PendingRevision;
            if (entry.Entity.PendingRevision <= revision)
            {
                entry.Entity.PendingRevision = null;
                entry.Entity.PendingSince = null;
            }
        }

        // Save before releasing the editing lock, including the PR tracking record if one changed.
        await database.SaveChangesAsync(cancellationToken);
    }

    private string BotEmail()
    {
        var emailOverride = configuration["Translations:BotEmail"];
        return string.IsNullOrWhiteSpace(emailOverride) ? "noreply@" + MainEmailDomain() : emailOverride;
    }

    private async Task<bool> WriteAndCommitFile(string checkout, string path, string languageCode, string original,
        string content, IReadOnlyCollection<TranslationChange> changes, IReadOnlyDictionary<long, User> authors,
        List<string> automatedFiles, CancellationToken cancellationToken)
    {
        if (content == original)
            return false;

        var fullPath = SafeFile(checkout, path);

        // TODO: do we need to preserve encoding or BOM marks?
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
        var languageChanges = changes.Where(change => change.LanguageCode == languageCode).ToArray();
        if (languageChanges.All(change => change.ActorUserId == null && change.ContributorUserId == null))
        {
            automatedFiles.Add(path);
            return false;
        }

        var contributors = Contributors(languageChanges, authors);
        var primary = contributors.FirstOrDefault();
        var message = new StringBuilder($"Update {languageCode} translations");
        foreach (var coauthor in contributors.Skip(1))
        {
            message.Append("\n\nCo-authored-by: ").Append(CreditName(coauthor)).Append(" <")
                .Append(CreditEmail(coauthor)).Append('>');
        }

        var environment = new Dictionary<string, string>
        {
            ["GIT_AUTHOR_NAME"] = primary == null ? "DevCenter Translations" : CreditName(primary),
            ["GIT_AUTHOR_EMAIL"] = primary == null ? BotEmail() : CreditEmail(primary),
            ["GIT_COMMITTER_NAME"] = "DevCenter Translations",
            ["GIT_COMMITTER_EMAIL"] = BotEmail(),
        };

        // TODO: Allow each translation project to configure whether commit checks should run.
        await repository.CommitFile(checkout, path, message.ToString(), environment, cancellationToken);
        return true;
    }

    private string? LastTranslator(string languageCode, IEnumerable<TranslationChange> changes,
        IReadOnlyDictionary<long, User> authors)
    {
        var primary = Contributors(changes.Where(change => change.LanguageCode == languageCode), authors)
            .FirstOrDefault();
        return primary == null ?
            null : $"{CreditName(primary)} <{CreditEmail(primary)}>";
    }

    private string CreditEmail(User user)
    {
        return user.UseEmailInTranslationCredits ?
            user.Email :
            $"noreply+{user.Id}@{MainEmailDomain()}";
    }

    private string MainEmailDomain()
    {
        var address = configuration["Email:FromAddress"];
        var at = address?.LastIndexOf('@') ?? -1;
        if (at < 0 || at == address!.Length - 1)
            throw new InvalidOperationException("Email:FromAddress is needed for translation commit credits");

        return address[(at + 1)..];
    }
}
