namespace ThriveDevCenter.Server.Models
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Services;
    using Shared;
    using Shared.Models;
    using Utilities;

    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Session> Sessions { get; set; }
        public DbSet<AccessKey> AccessKeys { get; set; }
        public DbSet<DehydratedObject> DehydratedObjects { get; set; }
        public DbSet<DevBuild> DevBuilds { get; set; }
        public DbSet<LauncherLink> LauncherLinks { get; set; }
        public DbSet<LfsObject> LfsObjects { get; set; }
        public DbSet<LfsProject> LfsProjects { get; set; }
        public DbSet<PatreonSettings> PatreonSettings { get; set; }
        public DbSet<Patron> Patrons { get; set; }
        public DbSet<ProjectGitFile> ProjectGitFiles { get; set; }
        public DbSet<StorageFile> StorageFiles { get; set; }
        public DbSet<StorageItem> StorageItems { get; set; }
        public DbSet<StorageItemVersion> StorageItemVersions { get; set; }
        public DbSet<RedeemableCode> RedeemableCodes { get; set; }
        public DbSet<AdminAction> AdminActions { get; set; }
        public DbSet<LogEntry> LogEntries { get; set; }
        public DbSet<GithubWebhook> GithubWebhooks { get; set; }
        public DbSet<CiProject> CiProjects { get; set; }
        public DbSet<CiBuild> CiBuilds { get; set; }
        public DbSet<CiJob> CiJobs { get; set; }
        public DbSet<CiJobArtifact> CiJobArtifacts { get; set; }
        public DbSet<ControlledServer> ControlledServers { get; set; }

        /// <summary>
        ///   If non-null this will be used to send model update notifications on save
        /// </summary>
        public IModelUpdateNotificationSender AutoSendNotifications { get; set; }

        public override int SaveChanges()
        {
            RunPreSaveChecks().Wait();

            return base.SaveChanges();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            // This runs the important checks synchronously before waiting for just notifications, so we can start the
            // db write before this completes fully
            var checkTask = RunPreSaveChecks();

            var result = await base.SaveChangesAsync(cancellationToken);

            await checkTask;

            return result;
        }

        public Task RunPreSaveChecks()
        {
            var changedEntities = ChangeTracker
                .Entries()
                .Where(e => e.State == EntityState.Added ||
                    e.State == EntityState.Modified || e.State == EntityState.Deleted).ToList();

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

            if (notifications != null)
            {
                var tasks = new List<Task> { Capacity = changedEntities.Count };

                foreach (var entry in changedEntities)
                {
                    if (entry.Entity is IUpdateNotifications notifiable)
                    {
                        bool previousSoftDeleted = false;

                        if (notifiable.UsesSoftDelete)
                            previousSoftDeleted = (bool)entry.OriginalValues[AppInfo.SoftDeleteAttribute];

                        tasks.Add(notifications.OnChangesDetected(entry.State, notifiable, previousSoftDeleted));
                    }
                }

                return Task.WhenAll(tasks);
            }

            return Task.CompletedTask;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            optionsBuilder.UseSnakeCaseNamingConvention();
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

                entity.Property(e => e.BuildOfTheDay).HasDefaultValue(false);
                entity.Property(e => e.Downloads).HasDefaultValue(0);
                entity.Property(e => e.Important).HasDefaultValue(false);
                entity.Property(e => e.Keep).HasDefaultValue(false);
                entity.Property(e => e.PrFetched).HasDefaultValue(false);
                entity.Property(e => e.Score).HasDefaultValue(0);
                entity.Property(e => e.Verified).HasDefaultValue(false);

                entity.HasOne(d => d.StorageItem)
                    .WithMany(p => p.DevBuilds).OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.VerifiedBy)
                    .WithMany(p => p.DevBuilds).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<LauncherLink>(entity =>
            {
                entity.Property(e => e.TotalApiCalls).HasDefaultValue(0);

                entity.HasOne(d => d.User)
                    .WithMany(p => p.LauncherLinks).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LfsObject>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo("lfs_objects_hilo");

                entity.HasOne(d => d.LfsProject)
                    .WithMany(p => p.LfsObjects).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LfsProject>(entity => { entity.Property(e => e.Deleted).HasDefaultValue(false); });

            modelBuilder.Entity<PatreonSettings>(entity => { });

            modelBuilder.Entity<Patron>(entity => { });

            modelBuilder.Entity<ProjectGitFile>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo("project_git_files_hilo");

                entity.HasOne(d => d.LfsProject)
                    .WithMany(p => p.ProjectGitFiles).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StorageFile>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo("storage_files_hilo");

                entity.Property(e => e.AllowParentless).HasDefaultValue(false);
            });

            modelBuilder.Entity<StorageItem>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo("storage_items_hilo");

                entity.HasIndex(e => e.Name, "index_storage_items_on_name")
                    .IsUnique()
                    .HasFilter("(parent_id IS NULL)");

                entity.Property(e => e.AllowParentless).HasDefaultValue(false);

                entity.Property(e => e.ReadAccess).HasDefaultValue(FileAccess.Developer);
                entity.Property(e => e.WriteAccess).HasDefaultValue(FileAccess.Developer);

                entity.Property(e => e.Special).HasDefaultValue(false);

                entity.HasOne(d => d.Owner)
                    .WithMany(p => p.StorageItems).OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(d => d.Parent)
                    .WithMany(p => p.Children)
                    .HasForeignKey(d => d.ParentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StorageItemVersion>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo("storage_item_versions_hilo");

                entity.Property(e => e.Keep).HasDefaultValue(false);

                entity.Property(e => e.Protected).HasDefaultValue(false);

                entity.Property(e => e.Version).HasDefaultValue(1);

                entity.HasOne(d => d.StorageFile)
                    .WithMany(p => p.StorageItemVersions).OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.StorageItem)
                    .WithMany(p => p.StorageItemVersions).OnDelete(DeleteBehavior.ClientCascade);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");

                entity.Property(e => e.Email).IsRequired();
                entity.Property(e => e.ApiToken).IsRequired(false);
                entity.Property(e => e.LfsToken).IsRequired(false);
                entity.Property(e => e.LauncherLinkCode).IsRequired(false);

                // TODO: add the non-null constraint later on once old rails data is imported
                entity.Property(e => e.UserName).HasColumnName("name");

                entity.Property(e => e.Suspended).HasDefaultValue(false);

                entity.Property(e => e.SuspendedManually).HasDefaultValue(false);

                entity.Property(e => e.TotalLauncherLinks).HasDefaultValue(0);

                entity.Property(e => e.SessionVersion).HasDefaultValue(1);
            });

            modelBuilder.Entity<Session>(entity =>
            {
                entity.HasOne(d => d.User).WithMany(p => p.Sessions).OnDelete(DeleteBehavior.Cascade);
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

            modelBuilder.Entity<CiProject>(entity =>
            {
                entity.HasMany(p => p.CiBuilds).WithOne(d => d.CiProject)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CiBuild>(entity =>
            {
                entity.HasKey(nameof(CiBuild.CiProjectId), nameof(CiBuild.CiBuildId));

                entity.Property(e => e.CreatedAt).HasDefaultValueSql("timezone('utc', now())");
                entity.Property(e => e.Status).HasDefaultValue(BuildStatus.Running);

                entity.HasMany(p => p.CiJobs).WithOne(d => d.Build)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CiJob>(entity =>
            {
                entity.HasKey(nameof(CiJob.CiProjectId), nameof(CiJob.CiBuildId), nameof(CiJob.CiJobId));

                entity.HasMany(p => p.CiJobArtifacts).WithOne(d => d.Job)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<CiJobArtifact>(entity =>
            {
                entity.HasKey(nameof(CiJobArtifact.CiProjectId), nameof(CiJobArtifact.CiBuildId),
                    nameof(CiJobArtifact.CiJobId), nameof(CiJobArtifact.CiJobArtifactId));
                entity.HasOne(d => d.StorageItem).WithMany(p => p.CiJobArtifacts)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<ControlledServer>(entity =>
            {
                entity.UseXminAsConcurrencyToken();
            });
        }
    }
}
