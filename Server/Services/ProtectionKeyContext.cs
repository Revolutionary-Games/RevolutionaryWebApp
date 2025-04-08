namespace RevolutionaryWebApp.Server.Services;

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public class ProtectionKeyContext(DbContextOptions<ProtectionKeyContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>
    ///   Map protection keys to a database table
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
}
