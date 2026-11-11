namespace RevolutionaryWebApp.Server.Services;

using System.Net;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
using Microsoft.EntityFrameworkCore;
using Models;

public static class RegistrationBlocks
{
    public static Task<bool> IsBlocked(ApplicationDbContext database, string email, IPAddress? address)
    {
        var normalized = Normalization.NormalizeEmail(email);
        var ip = NormalizeAddress(address);
        return database.RegistrationBlocks.AsNoTracking().AnyAsync(block =>
            block.NormalizedEmail == normalized || (ip != null && block.IPAddress == ip));
    }

    public static string? NormalizeAddress(IPAddress? address)
    {
        return address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4().ToString() : address?.ToString();
    }
}
