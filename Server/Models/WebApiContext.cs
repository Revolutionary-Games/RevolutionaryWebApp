namespace ThriveDevCenter.Server.Models
{
    using Microsoft.EntityFrameworkCore;

    public class WebApiContext : DbContext
    {
        public WebApiContext(DbContextOptions<WebApiContext> options) : base(options) { }

        public DbSet<AccessKey> AccessKeys { get; set; }
        public DbSet<DehydratedObject> DehydratedObjects { get; set; }
        public DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            optionsBuilder.UseSnakeCaseNamingConvention();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.UseIdentityColumns();

            // Need to manually specify defaults as the autogeneration doesn't seem to work with postgresql in EF
            modelBuilder.Entity<User>().Property(o => o.SessionVersion).HasDefaultValue(1);

            modelBuilder.Entity<User>().Property(o => o.CreatedAt).HasDefaultValueSql("timezone('utc', now())");
            modelBuilder.Entity<User>().Property(o => o.UpdatedAt).HasDefaultValueSql("timezone('utc', now())");

        }
    }
}
