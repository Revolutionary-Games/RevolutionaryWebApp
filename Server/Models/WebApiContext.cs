namespace ThriveDevCenter.Server.Models
{
    using Microsoft.EntityFrameworkCore;

    public class WebApiContext : DbContext
    {
        public WebApiContext(DbContextOptions<WebApiContext> options) : base(options) { }

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
        public DbSet<User> Users { get; set; }

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
            modelBuilder.Entity<User>().Property(o => o.SessionVersion).HasDefaultValue(1);

            modelBuilder.Entity<User>().Property(o => o.CreatedAt).HasDefaultValueSql("timezone('utc', now())");
            modelBuilder.Entity<User>().Property(o => o.UpdatedAt).HasDefaultValueSql("timezone('utc', now())");

                        modelBuilder.Entity<AccessKey>(entity =>
            {
                entity.ToTable("access_keys");

                entity.HasIndex(e => e.KeyCode, "index_access_keys_on_key_code")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Description)
                    .HasColumnType("character varying")
                    .HasColumnName("description");

                entity.Property(e => e.KeyCode)
                    .HasColumnType("character varying")
                    .HasColumnName("key_code");

                entity.Property(e => e.KeyType).HasColumnName("key_type");

                entity.Property(e => e.LastUsed).HasColumnName("last_used");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<DehydratedObject>(entity =>
            {
                entity.ToTable("dehydrated_objects");

                entity.HasIndex(e => e.Sha3, "index_dehydrated_objects_on_sha3")
                    .IsUnique();

                entity.HasIndex(e => e.StorageItemId, "index_dehydrated_objects_on_storage_item_id");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Sha3)
                    .HasColumnType("character varying")
                    .HasColumnName("sha3");

                entity.Property(e => e.StorageItemId).HasColumnName("storage_item_id");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.HasOne(d => d.StorageItem)
                    .WithMany(p => p.DehydratedObjects)
                    .HasForeignKey(d => d.StorageItemId)
                    .HasConstraintName("fk_rails_56afaae06f");
            });

            modelBuilder.Entity<DehydratedObjectsDevBuild>(entity =>
            {
                entity.HasNoKey();

                entity.ToTable("dehydrated_objects_dev_builds");

                entity.HasIndex(e => new { e.DehydratedObjectId, e.DevBuildId }, "dehydrated_objects_dev_builds_index_compound")
                    .IsUnique();

                entity.Property(e => e.DehydratedObjectId).HasColumnName("dehydrated_object_id");

                entity.Property(e => e.DevBuildId).HasColumnName("dev_build_id");
            });

            modelBuilder.Entity<DevBuild>(entity =>
            {
                entity.ToTable("dev_builds");

                entity.HasIndex(e => e.Anonymous, "index_dev_builds_on_anonymous");

                entity.HasIndex(e => new { e.BuildHash, e.Platform }, "index_dev_builds_on_build_hash_and_platform")
                    .IsUnique();

                entity.HasIndex(e => e.StorageItemId, "index_dev_builds_on_storage_item_id");

                entity.HasIndex(e => e.UserId, "index_dev_builds_on_user_id");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.Anonymous).HasColumnName("anonymous");

                entity.Property(e => e.Branch)
                    .HasColumnType("character varying")
                    .HasColumnName("branch");

                entity.Property(e => e.BuildHash)
                    .HasColumnType("character varying")
                    .HasColumnName("build_hash");

                entity.Property(e => e.BuildOfTheDay)
                    .HasColumnName("build_of_the_day")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.BuildZipHash)
                    .HasColumnType("character varying")
                    .HasColumnName("build_zip_hash");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Description)
                    .HasColumnType("character varying")
                    .HasColumnName("description");

                entity.Property(e => e.Downloads)
                    .HasColumnName("downloads")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.Important)
                    .HasColumnName("important")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.Keep)
                    .HasColumnName("keep")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.Platform)
                    .HasColumnType("character varying")
                    .HasColumnName("platform");

                entity.Property(e => e.PrFetched)
                    .HasColumnName("pr_fetched")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.PrUrl)
                    .HasColumnType("character varying")
                    .HasColumnName("pr_url");

                entity.Property(e => e.Score)
                    .HasColumnName("score")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.StorageItemId).HasColumnName("storage_item_id");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.Property(e => e.UserId).HasColumnName("user_id");

                entity.Property(e => e.Verified)
                    .HasColumnName("verified")
                    .HasDefaultValueSql("false");

                entity.HasOne(d => d.StorageItem)
                    .WithMany(p => p.DevBuilds)
                    .HasForeignKey(d => d.StorageItemId)
                    .HasConstraintName("fk_rails_3149f0b4a7");

                entity.HasOne(d => d.User)
                    .WithMany(p => p.DevBuilds)
                    .HasForeignKey(d => d.UserId)
                    .HasConstraintName("fk_rails_f73508aad4");
            });

            modelBuilder.Entity<LauncherLink>(entity =>
            {
                entity.ToTable("launcher_links");

                entity.HasIndex(e => e.LinkCode, "index_launcher_links_on_link_code")
                    .IsUnique();

                entity.HasIndex(e => e.UserId, "index_launcher_links_on_user_id");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.LastConnection).HasColumnName("last_connection");

                entity.Property(e => e.LastIp)
                    .HasColumnType("character varying")
                    .HasColumnName("last_ip");

                entity.Property(e => e.LinkCode)
                    .HasColumnType("character varying")
                    .HasColumnName("link_code");

                entity.Property(e => e.TotalApiCalls).HasColumnName("total_api_calls");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.Property(e => e.UserId).HasColumnName("user_id");

                entity.HasOne(d => d.User)
                    .WithMany(p => p.LauncherLinks)
                    .HasForeignKey(d => d.UserId)
                    .HasConstraintName("fk_rails_fe8a47e718");
            });

            modelBuilder.Entity<LfsObject>(entity =>
            {
                entity.ToTable("lfs_objects");

                entity.HasIndex(e => e.LfsProjectId, "index_lfs_objects_on_lfs_project_id");

                entity.HasIndex(e => new { e.LfsProjectId, e.Oid }, "index_lfs_objects_on_lfs_project_id_and_oid")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.LfsProjectId).HasColumnName("lfs_project_id");

                entity.Property(e => e.Oid)
                    .HasColumnType("character varying")
                    .HasColumnName("oid");

                entity.Property(e => e.Size).HasColumnName("size");

                entity.Property(e => e.StoragePath)
                    .HasColumnType("character varying")
                    .HasColumnName("storage_path");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.HasOne(d => d.LfsProject)
                    .WithMany(p => p.LfsObjects)
                    .HasForeignKey(d => d.LfsProjectId)
                    .HasConstraintName("fk_rails_a2a2f9af32");
            });

            modelBuilder.Entity<LfsProject>(entity =>
            {
                entity.ToTable("lfs_projects");

                entity.HasIndex(e => e.Name, "index_lfs_projects_on_name")
                    .IsUnique();

                entity.HasIndex(e => e.Slug, "index_lfs_projects_on_slug")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CloneUrl)
                    .HasColumnType("character varying")
                    .HasColumnName("clone_url");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.FileTreeCommit)
                    .HasColumnType("character varying")
                    .HasColumnName("file_tree_commit");

                entity.Property(e => e.FileTreeUpdated).HasColumnName("file_tree_updated");

                entity.Property(e => e.Name)
                    .HasColumnType("character varying")
                    .HasColumnName("name");

                entity.Property(e => e.Public).HasColumnName("public");

                entity.Property(e => e.RepoUrl)
                    .HasColumnType("character varying")
                    .HasColumnName("repo_url");

                entity.Property(e => e.Slug)
                    .HasColumnType("character varying")
                    .HasColumnName("slug");

                entity.Property(e => e.TotalObjectCount).HasColumnName("total_object_count");

                entity.Property(e => e.TotalObjectSize).HasColumnName("total_object_size");

                entity.Property(e => e.TotalSizeUpdated).HasColumnName("total_size_updated");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

            modelBuilder.Entity<PatreonSetting>(entity =>
            {
                entity.ToTable("patreon_settings");

                entity.HasIndex(e => e.WebhookId, "index_patreon_settings_on_webhook_id")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.Active).HasColumnName("active");

                entity.Property(e => e.CampaignId)
                    .HasColumnType("character varying")
                    .HasColumnName("campaign_id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.CreatorRefreshToken)
                    .HasColumnType("character varying")
                    .HasColumnName("creator_refresh_token");

                entity.Property(e => e.CreatorToken)
                    .HasColumnType("character varying")
                    .HasColumnName("creator_token");

                entity.Property(e => e.DevbuildsRewardId)
                    .HasColumnType("character varying")
                    .HasColumnName("devbuilds_reward_id");

                entity.Property(e => e.LastRefreshed).HasColumnName("last_refreshed");

                entity.Property(e => e.LastWebhook).HasColumnName("last_webhook");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.Property(e => e.VipRewardId)
                    .HasColumnType("character varying")
                    .HasColumnName("vip_reward_id");

                entity.Property(e => e.WebhookId)
                    .HasColumnType("character varying")
                    .HasColumnName("webhook_id");

                entity.Property(e => e.WebhookSecret)
                    .HasColumnType("character varying")
                    .HasColumnName("webhook_secret");
            });

            modelBuilder.Entity<Patron>(entity =>
            {
                entity.ToTable("patrons");

                entity.HasIndex(e => e.Email, "index_patrons_on_email")
                    .IsUnique();

                entity.HasIndex(e => e.EmailAlias, "index_patrons_on_email_alias")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Email)
                    .HasColumnType("character varying")
                    .HasColumnName("email");

                entity.Property(e => e.EmailAlias)
                    .HasColumnType("character varying")
                    .HasColumnName("email_alias");

                entity.Property(e => e.HasForumAccount).HasColumnName("has_forum_account");

                entity.Property(e => e.Marked).HasColumnName("marked");

                entity.Property(e => e.PatreonRefreshToken)
                    .HasColumnType("character varying")
                    .HasColumnName("patreon_refresh_token");

                entity.Property(e => e.PatreonToken)
                    .HasColumnType("character varying")
                    .HasColumnName("patreon_token");

                entity.Property(e => e.PledgeAmountCents).HasColumnName("pledge_amount_cents");

                entity.Property(e => e.RewardId)
                    .HasColumnType("character varying")
                    .HasColumnName("reward_id");

                entity.Property(e => e.Suspended).HasColumnName("suspended");

                entity.Property(e => e.SuspendedReason)
                    .HasColumnType("character varying")
                    .HasColumnName("suspended_reason");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.Property(e => e.Username)
                    .HasColumnType("character varying")
                    .HasColumnName("username");
            });

            modelBuilder.Entity<ProjectGitFile>(entity =>
            {
                entity.ToTable("project_git_files");

                entity.HasIndex(e => e.LfsProjectId, "index_project_git_files_on_lfs_project_id");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Ftype)
                    .HasColumnType("character varying")
                    .HasColumnName("ftype");

                entity.Property(e => e.LfsOid)
                    .HasColumnType("character varying")
                    .HasColumnName("lfs_oid");

                entity.Property(e => e.LfsProjectId).HasColumnName("lfs_project_id");

                entity.Property(e => e.Name)
                    .HasColumnType("character varying")
                    .HasColumnName("name");

                entity.Property(e => e.Path)
                    .HasColumnType("character varying")
                    .HasColumnName("path");

                entity.Property(e => e.Size).HasColumnName("size");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.HasOne(d => d.LfsProject)
                    .WithMany(p => p.ProjectGitFiles)
                    .HasForeignKey(d => d.LfsProjectId)
                    .HasConstraintName("fk_rails_c26d2f4b49");
            });

            modelBuilder.Entity<StorageFile>(entity =>
            {
                entity.ToTable("storage_files");

                entity.HasIndex(e => e.StoragePath, "index_storage_files_on_storage_path")
                    .IsUnique();

                entity.HasIndex(e => e.Uploading, "index_storage_files_on_uploading");

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.AllowParentless)
                    .HasColumnName("allow_parentless")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Size).HasColumnName("size");

                entity.Property(e => e.StoragePath)
                    .HasColumnType("character varying")
                    .HasColumnName("storage_path");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.Property(e => e.UploadExpires).HasColumnName("upload_expires");

                entity.Property(e => e.Uploading)
                    .HasColumnName("uploading")
                    .HasDefaultValueSql("true");
            });

            modelBuilder.Entity<StorageItem>(entity =>
            {
                entity.ToTable("storage_items");

                entity.HasIndex(e => e.AllowParentless, "index_storage_items_on_allow_parentless");

                entity.HasIndex(e => e.Name, "index_storage_items_on_name")
                    .IsUnique()
                    .HasFilter("(parent_id IS NULL)");

                entity.HasIndex(e => e.OwnerId, "index_storage_items_on_owner_id");

                entity.HasIndex(e => e.ParentId, "index_storage_items_on_parent_id");

                entity.HasIndex(e => new { e.ParentId, e.Name }, "index_storage_items_on_parent_id_and_name")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.AllowParentless)
                    .HasColumnName("allow_parentless")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Ftype).HasColumnName("ftype");

                entity.Property(e => e.Name)
                    .HasColumnType("character varying")
                    .HasColumnName("name");

                entity.Property(e => e.OwnerId).HasColumnName("owner_id");

                entity.Property(e => e.ParentId).HasColumnName("parent_id");

                entity.Property(e => e.ReadAccess)
                    .HasColumnName("read_access")
                    .HasDefaultValueSql("2");

                entity.Property(e => e.Size).HasColumnName("size");

                entity.Property(e => e.Special)
                    .HasColumnName("special")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.Property(e => e.WriteAccess)
                    .HasColumnName("write_access")
                    .HasDefaultValueSql("2");

                entity.HasOne(d => d.Owner)
                    .WithMany(p => p.StorageItems)
                    .HasForeignKey(d => d.OwnerId)
                    .HasConstraintName("fk_rails_90682aefbb");

                entity.HasOne(d => d.Parent)
                    .WithMany(p => p.InverseParent)
                    .HasForeignKey(d => d.ParentId)
                    .HasConstraintName("fk_rails_64a0997a94");
            });

            modelBuilder.Entity<StorageItemVersion>(entity =>
            {
                entity.ToTable("storage_item_versions");

                entity.HasIndex(e => e.StorageFileId, "index_storage_item_versions_on_storage_file_id");

                entity.HasIndex(e => e.StorageItemId, "index_storage_item_versions_on_storage_item_id");

                entity.HasIndex(e => new { e.StorageItemId, e.Version }, "index_storage_item_versions_on_storage_item_id_and_version")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Keep)
                    .HasColumnName("keep")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.Protected)
                    .HasColumnName("protected")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.StorageFileId).HasColumnName("storage_file_id");

                entity.Property(e => e.StorageItemId).HasColumnName("storage_item_id");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

                entity.Property(e => e.Uploading)
                    .HasColumnName("uploading")
                    .HasDefaultValueSql("true");

                entity.Property(e => e.Version)
                    .HasColumnName("version")
                    .HasDefaultValueSql("1");

                entity.HasOne(d => d.StorageFile)
                    .WithMany(p => p.StorageItemVersions)
                    .HasForeignKey(d => d.StorageFileId)
                    .HasConstraintName("fk_rails_f3c7a0fc29");

                entity.HasOne(d => d.StorageItem)
                    .WithMany(p => p.StorageItemVersions)
                    .HasForeignKey(d => d.StorageItemId)
                    .HasConstraintName("fk_rails_e9af7aab9b");
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");

                entity.HasIndex(e => e.ApiToken, "index_users_on_api_token")
                    .IsUnique();

                entity.HasIndex(e => e.Email, "index_users_on_email")
                    .IsUnique();

                entity.HasIndex(e => e.LauncherLinkCode, "index_users_on_launcher_link_code")
                    .IsUnique();

                entity.HasIndex(e => e.LfsToken, "index_users_on_lfs_token")
                    .IsUnique();

                entity.Property(e => e.Id).HasColumnName("id");

                entity.Property(e => e.Admin).HasColumnName("admin");

                entity.Property(e => e.ApiToken)
                    .HasColumnType("character varying")
                    .HasColumnName("api_token");

                entity.Property(e => e.CreatedAt).HasColumnName("created_at");

                entity.Property(e => e.Developer).HasColumnName("developer");

                entity.Property(e => e.Email)
                    .HasColumnType("character varying")
                    .HasColumnName("email");

                entity.Property(e => e.LauncherCodeExpires).HasColumnName("launcher_code_expires");

                entity.Property(e => e.LauncherLinkCode)
                    .HasColumnType("character varying")
                    .HasColumnName("launcher_link_code");

                entity.Property(e => e.LfsToken)
                    .HasColumnType("character varying")
                    .HasColumnName("lfs_token");

                entity.Property(e => e.Local).HasColumnName("local");

                entity.Property(e => e.Name)
                    .HasColumnType("character varying")
                    .HasColumnName("name");

                entity.Property(e => e.PasswordDigest)
                    .HasColumnType("character varying")
                    .HasColumnName("password_digest");

                entity.Property(e => e.SessionVersion)
                    .HasColumnName("session_version")
                    .HasDefaultValueSql("1");

                entity.Property(e => e.SsoSource)
                    .HasColumnType("character varying")
                    .HasColumnName("sso_source");

                entity.Property(e => e.Suspended)
                    .HasColumnName("suspended")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.SuspendedManually)
                    .HasColumnName("suspended_manually")
                    .HasDefaultValueSql("false");

                entity.Property(e => e.SuspendedReason)
                    .HasColumnType("character varying")
                    .HasColumnName("suspended_reason");

                entity.Property(e => e.TotalLauncherLinks)
                    .HasColumnName("total_launcher_links")
                    .HasDefaultValueSql("0");

                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            });

        }
    }
}
