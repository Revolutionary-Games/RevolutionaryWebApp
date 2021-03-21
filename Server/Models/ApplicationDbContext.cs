namespace ThriveDevCenter.Server.Models
{
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore;
    using Shared.Models;

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

            // Need to manually specify defaults as the auto generation doesn't seem to work with postgresql in EF
            // And also set more sensible on delete behaviours

            modelBuilder.Entity<DehydratedObject>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo();

                entity.HasOne(d => d.StorageItem)
                    .WithMany(p => p.DehydratedObjects).OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(p => p.DevBuilds).WithMany(p => p.DehydratedObjects)
                    .UsingEntity(j => j.ToTable("dehydrated_objects_dev_builds"));
            });

            modelBuilder.Entity<DevBuild>(entity =>
            {
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
                entity.Property(e => e.Id).UseHiLo();

                entity.HasOne(d => d.LfsProject)
                    .WithMany(p => p.LfsObjects).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<LfsProject>(entity => { });

            modelBuilder.Entity<PatreonSettings>(entity => { });

            modelBuilder.Entity<Patron>(entity => { });

            modelBuilder.Entity<ProjectGitFile>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo();

                entity.HasOne(d => d.LfsProject)
                    .WithMany(p => p.ProjectGitFiles).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StorageFile>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo();

                entity.Property(e => e.AllowParentless).HasDefaultValue(false);
            });

            modelBuilder.Entity<StorageItem>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo();

                entity.HasIndex(e => e.Name, "index_storage_items_on_name")
                    .IsUnique()
                    .HasFilter("(parent_id IS NULL)");

                entity.Property(e => e.AllowParentless).HasDefaultValue(false);

                entity.Property(e => e.ReadAccess).HasDefaultValue(2);
                entity.Property(e => e.WriteAccess).HasDefaultValue(2);

                entity.Property(e => e.Special).HasDefaultValue(false);

                entity.HasOne(d => d.Owner)
                    .WithMany(p => p.StorageItems).OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(d => d.Parent)
                    .WithMany(p => p.Children)
                    .HasForeignKey(d => d.ParentId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StorageItemVersion>(entity =>
            {
                entity.Property(e => e.Id).UseHiLo();

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

                // TODO: add the non-null constraint later on once old rails data is imported
                entity.Property(e => e.UserName).HasColumnName("name");

                entity.Property(e => e.Suspended).HasDefaultValue(false);

                entity.Property(e => e.SuspendedManually).HasDefaultValue(false);

                entity.Property(e => e.TotalLauncherLinks).HasDefaultValue(0);

                entity.Property(e => e.SessionVersion).HasDefaultValue(1);
            });

            modelBuilder.Entity<RedeemableCode>(entity =>
            {
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("timezone('utc', now())");
                entity.Property(e => e.MultiUse).HasDefaultValue(false);
                entity.Property(e => e.MultiUse).HasDefaultValue(false);
                entity.Property(e => e.Uses).HasDefaultValue(0);
            });
        }
    }
}
