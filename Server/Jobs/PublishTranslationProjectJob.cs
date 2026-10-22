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
using SharedBase.Utilities;

/// <summary>
///   Regenerates an app-owned PR branch from the latest public upstream branch.
/// </summary>
[DisableConcurrentExecution(600)]
public sealed class PublishTranslationProjectJob
{
    private readonly ApplicationDbContext database;
    private readonly ILocalTempFileLocks fileLocks;
    private readonly ITranslationGithubTokenProvider tokenProvider;
    private readonly IConfiguration configuration;
    private readonly ILogger<PublishTranslationProjectJob> logger;

    public PublishTranslationProjectJob(ApplicationDbContext database, ILocalTempFileLocks fileLocks,
        ITranslationGithubTokenProvider tokenProvider, IConfiguration configuration,
        ILogger<PublishTranslationProjectJob> logger)
    {
        this.database = database;
        this.fileLocks = fileLocks;
        this.tokenProvider = tokenProvider;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task Execute(long projectId, CancellationToken cancellationToken)
    {
        var project = await database.TranslationProjects.FindAsync([projectId], cancellationToken);
        if (project == null || !project.Enabled || project.Format == TranslationFormat.Manual)
            return;

        var token = await tokenProvider.GetToken(cancellationToken) ??
            throw new InvalidOperationException("Translation publishing GitHub token is not configured");
        var checkout = fileLocks.GetTempFilePath($"translationRepos/{project.Id}");
        using (await fileLocks.LockAsync(checkout, cancellationToken))
        {
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

    private static void MarkPublished(IEnumerable<TranslationValue> pending, long revision)
    {
        foreach (var value in pending.Where(v => v.PendingRevision <= revision))
        {
            value.LastPublishedRevision = value.PendingRevision;
            value.PendingRevision = null;
            value.PendingSince = null;
        }
    }

    private static string CreditName(User user)
    {
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
        return name.Replace('\r', ' ').Replace('\n', ' ').Replace('<', ' ').Replace('>', ' ');
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

    private async Task Publish(TranslationProject project, string checkout, string token,
        TranslationSyncRun run, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.RepositoryFullName) || string.IsNullOrEmpty(project.SourceBranch) ||
            string.IsNullOrEmpty(project.SourceFilePattern) || string.IsNullOrEmpty(project.TranslationFilePattern))
        {
            throw new InvalidOperationException("Translation repository configuration is incomplete");
        }

        if (await database.TranslationImportConflicts.AnyAsync(c => c.ProjectId == project.Id &&
                c.ResolvedAt == null, cancellationToken))
        {
            throw new InvalidOperationException("Resolve translation import conflicts before publishing");
        }

        var values = await database.TranslationValues.Where(v => v.ProjectId == project.Id)
            .ToListAsync(cancellationToken);
        var pending = values.Where(v => v.PendingRevision != null).ToArray();
        var snapshotRevision = project.CurrentRevision;
        await GitRunHelpers.EnsureRepoIsCloned($"https://github.com/{project.RepositoryFullName}.git", checkout,
            true, cancellationToken);
        await GitRunHelpers.FetchRef(checkout, $"refs/heads/{project.SourceBranch}", cancellationToken);
        await GitRunHelpers.Checkout(checkout, $"origin/{project.SourceBranch}", true, cancellationToken, true);
        await GitRunHelpers.Clean(checkout, cancellationToken);
        var upstreamSha = await GitRunHelpers.GetCurrentCommit(checkout, cancellationToken);
        run.UpstreamSha = upstreamSha;
        if (upstreamSha != project.LastImportedSha)
        {
            throw new InvalidOperationException(
                "Upstream changed since import; import the new commit before publishing");
        }

        using var github = new TranslationGithubClient(token);
        var forkOwner = await github.GetLogin(cancellationToken);
        await github.EnsureFork(project.RepositoryFullName, forkOwner, cancellationToken);
        var tracked = await database.TranslationPullRequests.FindAsync([project.Id],
            cancellationToken);
        if (pending.Length == 0 && tracked is not { Open: true })
            return;

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
        await GitRunHelpers.CheckoutNewBranch(checkout, branch, $"origin/{project.SourceBranch}", true,
            cancellationToken);

        var sourceContent = await File.ReadAllTextAsync(SafeFile(checkout, project.SourceFilePattern),
            cancellationToken);
        var languages = await database.TranslationProjectLanguages.AsNoTracking()
            .Where(l => l.ProjectId == project.Id && l.Enabled && l.LanguageCode != project.SourceLanguage)
            .OrderBy(l => l.LanguageCode).ToListAsync(cancellationToken);
        var contributedSince = tracked?.LastPublishedRevision ?? 0;
        var changes = await database.TranslationChanges.AsNoTracking()
            .Where(c => c.ProjectId == project.Id && c.ProjectRevision > contributedSince &&
                c.ProjectRevision <= snapshotRevision && c.CountsAsContribution && c.ContributorUserId != null)
            .ToListAsync(cancellationToken);
        var authors = await database.Users.AsNoTracking().Where(u =>
            changes.Select(c => c.ContributorUserId).Contains(u.Id)).ToDictionaryAsync(u => u.Id,
            cancellationToken);
        var commitCount = 0;

        foreach (var language in languages)
        {
            var path = language.FilePath ?? project.TranslationFilePattern.Replace("{language}",
                language.LanguageCode, StringComparison.Ordinal);
            var fullPath = SafeFile(checkout, path);
            var original = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var localeValues = values.Where(v => v.LanguageCode == language.LanguageCode).ToArray();
            var content = project.Format switch
            {
                TranslationFormat.Gettext => TranslationFileExporter.ExportGettext(sourceContent,
                    original, localeValues),
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

            // TODO: do we need to preserve encoding or BOM marks?
            await File.WriteAllTextAsync(fullPath, content, cancellationToken);
            await GitRunHelpers.Add(checkout, path, cancellationToken);
            var contributors = changes.Where(c => c.LanguageCode == language.LanguageCode)
                .GroupBy(c => c.ContributorUserId!.Value).OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key).Select(g => authors.GetValueOrDefault(g.Key)).Where(u => u != null)
                .ToArray();
            var primary = contributors.FirstOrDefault();
            var message = new StringBuilder($"Update {language.LanguageCode} translations");
            foreach (var coauthor in contributors.Skip(1))
            {
                message.Append("\n\nCo-authored-by: ").Append(CreditName(coauthor!)).Append(" <")
                    .Append(CreditEmail(coauthor!)).Append('>');
            }

            var environment = new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = primary == null ? "DevCenter Translations" : CreditName(primary),
                ["GIT_AUTHOR_EMAIL"] = primary == null ? BotEmail() : CreditEmail(primary),
                ["GIT_COMMITTER_NAME"] = "DevCenter Translations",
                ["GIT_COMMITTER_EMAIL"] = BotEmail(),
            };

            // TODO: Allow each translation project to configure whether commit checks should run.
            await GitRunHelpers.Commit(checkout, message.ToString(), path, environment, cancellationToken,
                noVerify: true);
            ++commitCount;
        }

        if (commitCount == 0)
        {
            MarkPublished(pending, snapshotRevision);
            return;
        }

        var headSha = await GitRunHelpers.GetCurrentCommit(checkout, cancellationToken);
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
        var authEnvironment = new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {auth}",
        };
        var expected = reusePr ? tracked!.HeadSha ?? string.Empty : string.Empty;
        await GitRunHelpers.PushBranch(checkout,
            $"https://github.com/{forkOwner}/{project.RepositoryFullName.Split('/')[1]}.git", branch, expected,
            authEnvironment, cancellationToken);

        var pr = reusePr ?
            remotePr! :
            await github.CreatePullRequest(project.RepositoryFullName,
                project.SourceBranch, forkOwner, branch, cancellationToken);
        tracked ??= new TranslationPullRequest { ProjectId = project.Id };
        if (database.Entry(tracked).State == EntityState.Detached)
            database.TranslationPullRequests.Add(tracked);
        tracked.Number = pr.Number;
        tracked.HeadBranch = branch;
        tracked.HeadSha = headSha;
        tracked.Open = true;
        tracked.LastCheckedAt = DateTime.UtcNow;
        tracked.LastPublishedRevision = snapshotRevision;
        MarkPublished(pending, snapshotRevision);
    }

    private string BotEmail()
    {
        return "noreply@" + MainEmailDomain();
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
