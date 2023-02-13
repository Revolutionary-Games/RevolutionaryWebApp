namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Enums;
using Microsoft.EntityFrameworkCore;
using Services;
using Shared;
using Shared.Notifications;
using Utilities;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // These are automatically initialized by EF, we use null forgiving here to silence warnings
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Session> Sessions { get; set; } = null!;
    public DbSet<AccessKey> AccessKeys { get; set; } = null!;
    public DbSet<DehydratedObject> DehydratedObjects { get; set; } = null!;
    public DbSet<DevBuild> DevBuilds { get; set; } = null!;
    public DbSet<LauncherLink> LauncherLinks { get; set; } = null!;
    public DbSet<LfsObject> LfsObjects { get; set; } = null!;
    public DbSet<LfsProject> LfsProjects { get; set; } = null!;
    public DbSet<PatreonSettings> PatreonSettings { get; set; } = null!;
    public DbSet<Patron> Patrons { get; set; } = null!;
    public DbSet<ProjectGitFile> ProjectGitFiles { get; set; } = null!;
    public DbSet<StorageFile> StorageFiles { get; set; } = null!;
    public DbSet<StorageItem> StorageItems { get; set; } = null!;
    public DbSet<StorageItemVersion> StorageItemVersions { get; set; } = null!;
    public DbSet<RedeemableCode> RedeemableCodes { get; set; } = null!;
    public DbSet<AdminAction> AdminActions { get; set; } = null!;
    public DbSet<LogEntry> LogEntries { get; set; } = null!;
    public DbSet<ActionLogEntry> ActionLogEntries { get; set; } = null!;
    public DbSet<GithubWebhook> GithubWebhooks { get; set; } = null!;
    public DbSet<CiProject> CiProjects { get; set; } = null!;
    public DbSet<CiSecret> CiSecrets { get; set; } = null!;
    public DbSet<CiBuild> CiBuilds { get; set; } = null!;
    public DbSet<CiJob> CiJobs { get; set; } = null!;
    public DbSet<CiJobArtifact> CiJobArtifacts { get; set; } = null!;
    public DbSet<CiJobOutputSection> CiJobOutputSections { get; set; } = null!;
    public DbSet<ControlledServer> ControlledServers { get; set; } = null!;
    public DbSet<ExternalServer> ExternalServers { get; set; } = null!;
    public DbSet<Cla> Clas { get; set; } = null!;
    public DbSet<ClaSignature> ClaSignatures { get; set; } = null!;
    public DbSet<Meeting> Meetings { get; set; } = null!;
    public DbSet<MeetingMember> MeetingMembers { get; set; } = null!;
    public DbSet<MeetingPoll> MeetingPolls { get; set; } = null!;
    public DbSet<MeetingPollVote> MeetingPollVotes { get; set; } = null!;
    public DbSet<MeetingPollVotingRecord> MeetingPollVotingRecords { get; set; } = null!;
    public DbSet<InProgressClaSignature> InProgressClaSignatures { get; set; } = null!;
    public DbSet<InProgressMultipartUpload> InProgressMultipartUploads { get; set; } = null!;
    public DbSet<GithubAutoComment> GithubAutoComments { get; set; } = null!;
    public DbSet<GithubPullRequest> GithubPullRequests { get; set; } = null!;
    public DbSet<SentBulkEmail> SentBulkEmails { get; set; } = null!;
    public DbSet<CrashReport> CrashReports { get; set; } = null!;
    public DbSet<StackwalkTask> StackwalkTasks { get; set; } = null!;
    public DbSet<DebugSymbol> DebugSymbols { get; set; } = null!;
    public DbSet<Backup> Backups { get; set; } = null!;
    public DbSet<AssociationMember> AssociationMembers { get; set; } = null!;
    public DbSet<GlobalDiscordBotCommand> GlobalDiscordBotCommands { get; set; } = null!;
    public DbSet<WatchedKeyword> WatchedKeywords { get; set; } = null!;
    public DbSet<RepoForReleaseStats> ReposForReleaseStats { get; set; } = null!;
    public DbSet<Feed> Feeds { get; set; } = null!;
    public DbSet<FeedDiscordWebhook> FeedDiscordWebhooks { get; set; } = null!;
    public DbSet<SeenFeedItem> SeenFeedItems { get; set; } = null!;
    public DbSet<CombinedFeed> CombinedFeeds { get; set; } = null!;
    public DbSet<LauncherDownloadMirror> LauncherDownloadMirrors { get; set; } = null!;
    public DbSet<LauncherLauncherVersion> LauncherLauncherVersions { get; set; } = null!;
    public DbSet<LauncherVersionAutoUpdateChannel> LauncherVersionAutoUpdateChannels { get; set; } = null!;
    public DbSet<LauncherVersionDownload> LauncherVersionDownloads { get; set; } = null!;
    public DbSet<LauncherThriveVersion> LauncherThriveVersions { get; set; } = null!;
    public DbSet<LauncherThriveVersionPlatform> LauncherThriveVersionPlatforms { get; set; } = null!;
    public DbSet<LauncherThriveVersionDownload> LauncherThriveVersionDownloads { get; set; } = null!;

    /// <summary>
    ///   If non-null this will be used to send model update notifications on save
    /// </summary>
    public IModelUpdateNotificationSender? AutoSendNotifications { get; set; }

    public override int SaveChanges()
    {
        var notificationsToSend = RunPreSaveChecks();

        var result = base.SaveChanges();

        SendUpdateNotifications(notificationsToSend).Wait();

        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Run pre-save validations and build notifications before saving
        var notificationsToSend = RunPreSaveChecks();

        var result = await base.SaveChangesAsync(cancellationToken);

        await SendUpdateNotifications(notificationsToSend);

        return result;
    }

    public List<Tuple<SerializedNotification, string>>? RunPreSaveChecks()
    {
        var changedEntities = ChangeTracker
            .Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();

        foreach (var entry in changedEntities)
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                continue;

            if (entry.Entity is IContainsHashedLookUps containsHashedLookUps)
            {
                containsHashedLookUps.ComputeHashedLookUpValues();
            }
        }

        // TODO: do we want to run the validations here?
        // var errors = new List<ValidationResult>();
        // foreach (var e in changedEntities)
        // {
        //     Validator.TryValidateObject(
        //         e.Entity, new ValidationContext(e.Entity), errors, true);
        // }

        var notifications = AutoSendNotifications;

        if (notifications == null)
            return null;

        // Build notifications
        var notificationsToSend = new List<Tuple<SerializedNotification, string>>();

        foreach (var entry in changedEntities)
        {
            if (entry.Entity is IUpdateNotifications notifiable)
            {
                bool previousSoftDeleted = false;

                if (notifiable.UsesSoftDelete)
                    previousSoftDeleted = (bool)entry.OriginalValues[AppInfo.SoftDeleteAttribute]!;

                notificationsToSend.AddRange(notifications.OnChangesDetected(entry.State, notifiable,
                    previousSoftDeleted));
            }
        }

        return notificationsToSend;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasAnnotation("Relational:Collation", "en_GB.UTF-8");
        modelBuilder.UseIdentityColumns();
        modelBuilder.UseDatabaseTemplate("template0");

        // Need to manually specify defaults as the auto generation doesn't seem to work with postgresql in EF
        // And also set more sensible on delete behaviours

        modelBuilder.Entity<DehydratedObject>(entity =>
        {
            entity.Property(e => e.Id).UseHiLo("dehydrated_objects_hilo");

            entity.HasOne(d => d.StorageItem)
                .WithMany(p => p.DehydratedObjects).OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.DevBuilds).WithMany(p => p.DehydratedObjects)
                .UsingEntity(j => j.ToTable("dehydrated_objects_dev_builds"));
        });

        modelBuilder.Entity<DevBuild>(entity =>
        {
            // Seems like adding custom validation on a property requires this to mark it as nullable
            entity.Property(e => e.Description).IsRequired(false);

            entity.HasOne(d => d.StorageItem)
                .WithMany(p => p.DevBuilds).OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.VerifiedBy)
                .WithMany(p => p.DevBuilds).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LauncherLink>(entity =>
        {
            entity.HasOne(d => d.User)
                .WithMany(p => p.LauncherLinks).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LfsObject>(entity =>
        {
            entity.Property(e => e.Id).UseHiLo("lfs_objects_hilo");

            entity.HasOne(d => d.LfsProject)
                .WithMany(p => p.LfsObjects).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LfsProject>(entity =>
        {
            entity.Property(e => e.BranchToBuildFileTreeFor).HasDefaultValue("master");
        });

        // modelBuilder.Entity<PatreonSettings>(entity => { });

        // modelBuilder.Entity<Patron>(entity => { });

        modelBuilder.Entity<ProjectGitFile>(entity =>
        {
            entity.Property(e => e.Id).UseHiLo("project_git_files_hilo");

            entity.HasOne(d => d.LfsProject)
                .WithMany(p => p.ProjectGitFiles).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StorageFile>(entity => { entity.Property(e => e.Id).UseHiLo("storage_files_hilo"); });

        modelBuilder.Entity<StorageItem>(entity =>
        {
            entity.Property(e => e.Id).UseHiLo("storage_items_hilo");

            entity.HasIndex(e => e.Name, "index_storage_items_on_name")
                .IsUnique()
                .HasFilter("(parent_id IS NULL)");

            entity.HasOne(d => d.Owner)
                .WithMany(p => p.StorageItems).OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(d => d.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StorageItemVersion>(entity =>
        {
            entity.Property(e => e.Id).UseHiLo("storage_item_versions_hilo");

            entity.HasOne(d => d.StorageFile)
                .WithMany(p => p.StorageItemVersions).OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.StorageItem)
                .WithMany(p => p.StorageItemVersions).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.ApiToken).IsRequired(false);
            entity.Property(e => e.LfsToken).IsRequired(false);
            entity.Property(e => e.LauncherLinkCode).IsRequired(false);

            entity.Property(e => e.UserName).HasColumnName("name").IsRequired().HasDefaultValue("UNKNOWN");

            entity.Property(e => e.Suspended).HasDefaultValue(false);
            entity.Property(e => e.Restricted).HasDefaultValue(false);

            entity.Property(e => e.SuspendedManually).HasDefaultValue(false);
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasOne(d => d.User).WithMany(p => p.Sessions).OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.StartedAt).HasDefaultValueSql("timezone('utc', now())");
        });

        modelBuilder.Entity<RedeemableCode>(entity =>
        {
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("timezone('utc', now())");
            entity.Property(e => e.MultiUse).HasDefaultValue(false);
            entity.Property(e => e.MultiUse).HasDefaultValue(false);
            entity.Property(e => e.Uses).HasDefaultValue(0);
        });

        modelBuilder.Entity<LogEntry>(entity =>
        {
            entity.HasOne(d => d.TargetUser).WithMany(p => p.TargetedInLogs).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AdminAction>(entity =>
        {
            entity.HasOne(d => d.TargetUser).WithMany(p => p.TargetedByAdminActions)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.PerformedBy).WithMany(p => p.PerformedAdminActions)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ActionLogEntry>(entity =>
        {
            entity.HasOne(d => d.PerformedBy).WithMany(p => p.PerformedActions)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CiProject>(entity =>
        {
            entity.HasMany(p => p.CiBuilds).WithOne(d => d.CiProject)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.CiSecrets).WithOne(d => d.CiProject)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CiSecret>(entity =>
        {
            entity.HasKey(nameof(CiSecret.CiProjectId), nameof(CiSecret.CiSecretId));
        });

        modelBuilder.Entity<CiBuild>(entity =>
        {
            entity.HasKey(nameof(CiBuild.CiProjectId), nameof(CiBuild.CiBuildId));

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("timezone('utc', now())");

            entity.HasMany(p => p.CiJobs).WithOne(d => d.Build)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CiJob>(entity =>
        {
            entity.HasKey(nameof(CiJob.CiProjectId), nameof(CiJob.CiBuildId), nameof(CiJob.CiJobId));

            entity.HasMany(p => p.CiJobArtifacts).WithOne(d => d.Job)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(p => p.CiJobOutputSections).WithOne(d => d.Job)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("timezone('utc', now())");
        });

        modelBuilder.Entity<CiJobArtifact>(entity =>
        {
            entity.HasKey(nameof(CiJobArtifact.CiProjectId), nameof(CiJobArtifact.CiBuildId),
                nameof(CiJobArtifact.CiJobId), nameof(CiJobArtifact.CiJobArtifactId));
            entity.HasOne(d => d.StorageItem).WithMany(p => p.CiJobArtifacts)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CiJobOutputSection>(entity =>
        {
            entity.HasKey(nameof(CiJobOutputSection.CiProjectId), nameof(CiJobOutputSection.CiBuildId),
                nameof(CiJobOutputSection.CiJobId), nameof(CiJobOutputSection.CiJobOutputSectionId));

            entity.Property(e => e.StartedAt).HasDefaultValueSql("timezone('utc', now())");
        });

        modelBuilder.Entity<ControlledServer>(entity => { entity.Property(e => e.UsedDiskSpace).HasDefaultValue(-1); });

        modelBuilder.Entity<ExternalServer>(entity =>
        {
            entity.Property(e => e.UsedDiskSpace).HasDefaultValue(-1);
            entity.Property(e => e.Priority).HasDefaultValue(0);

            // Mapping IP to string is required for use with unique index
            // https://github.com/dotnet/efcore/issues/23775
            entity.Property(e => e.PublicAddress).IsRequired()
                .HasConversion(a => a != null ? a.ToString() : null, s => s != null ? IPAddress.Parse(s) : null);
        });

        modelBuilder.Entity<Cla>(entity =>
        {
            entity.HasMany(p => p.Signatures).WithOne(d => d.Cla).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClaSignature>(entity =>
        {
            entity.HasOne(d => d.User)
                .WithMany(p => p.ClaSignatures).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Meeting>(entity =>
        {
            entity.HasOne(d => d.Owner)
                .WithMany(p => p.OwnerOfMeetings).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.Secretary)
                .WithMany(p => p.SecretaryOfMeetings).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.Chairman)
                .WithMany(p => p.ChairmanOfMeetings).OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(p => p.MeetingMembers)
                .WithOne(d => d.Meeting).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MeetingMember>(entity =>
        {
            entity.HasKey(nameof(MeetingMember.MeetingId), nameof(MeetingMember.UserId));
            entity.HasOne(d => d.User)
                .WithMany(p => p.MemberOfMeetings).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MeetingPoll>(entity =>
        {
            entity.HasKey(nameof(MeetingPoll.MeetingId), nameof(MeetingPoll.PollId));

            entity.HasOne(p => p.Meeting)
                .WithMany(d => d.MeetingPolls).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.Votes)
                .WithOne(d => d.Poll).OnDelete(DeleteBehavior.Cascade)
                .HasForeignKey(nameof(MeetingPollVote.MeetingId), nameof(MeetingPollVote.PollId));
            entity.HasMany(p => p.VotingRecords)
                .WithOne(d => d.Poll).OnDelete(DeleteBehavior.Cascade)
                .HasForeignKey(nameof(MeetingPollVotingRecord.MeetingId), nameof(MeetingPollVotingRecord.PollId));

            entity.HasOne(p => p.ManuallyClosedBy)
                .WithMany(d => d.ManuallyClosedPolls).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MeetingPollVote>(entity =>
        {
            entity.HasOne(d => d.Meeting)
                .WithMany(p => p.MeetingPollVotes).OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Meeting)
                .WithMany(p => p.MeetingPollVotes).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MeetingPollVotingRecord>(entity =>
        {
            entity.HasKey(nameof(MeetingPollVotingRecord.MeetingId), nameof(MeetingPollVotingRecord.PollId),
                nameof(MeetingPollVotingRecord.UserId));

            entity.HasOne(d => d.Meeting)
                .WithMany(p => p.MeetingPollVotingRecords).OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.User)
                .WithMany(p => p.VotedInPollsRecords).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InProgressClaSignature>(entity =>
        {
            entity.HasOne(d => d.Session)
                .WithOne(p => p.InProgressClaSignature).OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Cla).WithMany(p => p.InProgressSignatures).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GithubAutoComment>(entity =>
        {
            entity.HasMany(d => d.PostedOnPullRequests).WithMany(p => p.AutoComments);
        });

        modelBuilder.Entity<SentBulkEmail>(entity =>
        {
            entity.HasOne(d => d.SentBy).WithMany(p => p.SentBulkEmails).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CrashReport>(entity =>
        {
            entity.HasOne(d => d.DescriptionLastEditedBy).WithMany(p => p.LastEditedCrashReportDescriptions)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DebugSymbol>(entity =>
        {
            entity.HasOne(d => d.StoredInItem).WithMany(p => p.DebugSymbols).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(d => d.CreatedBy).WithMany(p => p.CreatedDebugSymbols).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AssociationMember>(entity =>
        {
            entity.HasOne(d => d.User).WithOne(p => p.AssociationMember).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Feed>(entity =>
        {
            entity.HasMany(p => p.SeenFeedItems).WithOne(d => d.Feed).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(p => p.DiscordWebhooks).WithOne(d => d.Feed).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FeedDiscordWebhook>(entity =>
        {
            entity.HasKey(nameof(FeedDiscordWebhook.FeedId), nameof(FeedDiscordWebhook.WebhookUrl));
        });

        modelBuilder.Entity<SeenFeedItem>(entity =>
        {
            entity.HasKey(nameof(SeenFeedItem.FeedId), nameof(SeenFeedItem.ItemIdentifier));
        });

        modelBuilder.Entity<CombinedFeed>(entity =>
        {
            entity.HasMany(d => d.CombinedFromFeeds).WithMany(p => p.CombinedInto);
        });

        // Start of launcher info models, first the mirrors
        modelBuilder.Entity<LauncherDownloadMirror>(entity =>
        {
            entity.HasMany(p => p.LauncherVersionDownloads).WithOne(d => d.Mirror)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(p => p.ThriveVersionDownloads).WithOne(d => d.Mirror)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Launcher downloads
        modelBuilder.Entity<LauncherLauncherVersion>(entity =>
        {
            entity.HasMany(p => p.AutoUpdateDownloads).WithOne(d => d.Version).OnDelete(DeleteBehavior.Cascade);

            // Ensures only one true value can exist
            entity.HasIndex(e => e.Latest)
                .IsUnique()
                .HasFilter("(latest IS TRUE)");
        });

        modelBuilder.Entity<LauncherVersionAutoUpdateChannel>(entity =>
        {
            entity.HasKey(nameof(LauncherVersionAutoUpdateChannel.VersionId),
                nameof(LauncherVersionAutoUpdateChannel.Channel));

            entity.HasMany(p => p.Mirrors).WithOne(d => d.UpdateChannel)
                .HasForeignKey(nameof(LauncherVersionDownload.VersionId), nameof(LauncherVersionDownload.Channel))
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LauncherVersionDownload>(entity =>
        {
            entity.HasKey(nameof(LauncherVersionDownload.VersionId),
                nameof(LauncherVersionDownload.Channel), nameof(LauncherVersionDownload.MirrorId));
        });

        // Thrive downloads
        modelBuilder.Entity<LauncherThriveVersion>(entity =>
        {
            entity.HasMany(p => p.Platforms).WithOne(d => d.Version).OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.Stable, e.Latest })
                .IsUnique()
                .HasFilter("(latest IS TRUE)");
        });

        modelBuilder.Entity<LauncherThriveVersionPlatform>(entity =>
        {
            entity.HasKey(nameof(LauncherThriveVersionPlatform.VersionId),
                nameof(LauncherThriveVersionPlatform.Platform));

            entity.HasMany(p => p.Mirrors).WithOne(d => d.PartOfPlatform)
                .HasForeignKey(nameof(LauncherThriveVersionDownload.VersionId),
                    nameof(LauncherThriveVersionDownload.Platform)).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LauncherThriveVersionDownload>(entity =>
        {
            entity.HasKey(nameof(LauncherThriveVersionDownload.VersionId),
                nameof(LauncherThriveVersionDownload.Platform), nameof(LauncherThriveVersionDownload.MirrorId));
        });
    }

    private Task SendUpdateNotifications(List<Tuple<SerializedNotification, string>>? messages)
    {
        var notifications = AutoSendNotifications;

        if (notifications == null || messages == null || messages.Count < 1)
            return Task.CompletedTask;

        return notifications.SendNotifications(messages);
    }
}
