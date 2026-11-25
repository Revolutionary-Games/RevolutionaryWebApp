namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.Utilities;
using StackExchange.Redis;

public class DownloadQuota
{
    public const string LimitReachedMessage = "Weekly download quota reached. Wait for your quota to reset.";

    public const string PublicLfsLimitReachedMessage =
        "Login required: the public IP download quota was reached. Log in or wait a week to download more.";

    /// <summary>
    ///   Download limit configuration for non-logged in users.
    /// </summary>
    private const long PublicLimit = 4500L * GlobalConstants.MEBIBYTE;

    private const int WeekInSeconds = 7 * 24 * 60 * 60;

    /// <summary>
    ///   We use a Redis script to do quota check in a single operation.
    /// </summary>
    private const string ReserveScript = """
                                         local used = redis.call('GET', KEYS[1])
                                         if tonumber(ARGV[1]) > tonumber(ARGV[2]) - tonumber(used or '0') then
                                             return 0
                                         end
                                         if used then
                                             redis.call('INCRBY', KEYS[1], ARGV[1])
                                         else
                                             redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[3])
                                         end
                                         return 1
                                         """;

    private readonly HashSet<string> exemptIps = new(StringComparer.Ordinal);
    private readonly IDatabase? database;
    private readonly ILogger<DownloadQuota> logger;

    public DownloadQuota(IConfiguration configuration, ILogger<DownloadQuota> logger,
        IConnectionMultiplexer? redis = null)
    {
        database = redis?.GetDatabase();
        this.logger = logger;

        var addresses = configuration["DownloadQuota:ExemptIPs"] ?? "::1,127.0.0.1";
        foreach (var entry in addresses.Split(',',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (IPAddress.TryParse(entry, out var address))
            {
                exemptIps.Add(RegistrationBlocks.NormalizeAddress(address)!);
            }
            else
            {
                logger.LogWarning("Ignoring invalid download quota IP exemption: {Address}", entry);
            }
        }
    }

    public static long GetLimit(User? user)
    {
        if (user == null)
            return PublicLimit;

        var groups = user.AccessCachedGroupsOrThrow();
        if (groups.HasAccessLevel(GroupType.Admin))
            return 1000 * (long)GlobalConstants.GIBIBYTE;

        if (groups.HasGroup(GroupType.PatreonSupporter))
            return 500 * (long)GlobalConstants.GIBIBYTE;

        if (groups.HasAccessLevel(GroupType.Developer))
            return 100 * (long)GlobalConstants.GIBIBYTE;

        return 2 * PublicLimit;
    }

    public async Task<long?> GetUsageAsync(long userId)
    {
        if (database == null)
            return null;

        try
        {
            var used = await database.StringGetAsync(UserKey(userId));
            return (long?)used ?? 0;
        }
        catch (Exception e) when (e is RedisException or TimeoutException)
        {
            logger.LogError(e, "Could not read download quota usage");
            return null;
        }
    }

    public async Task<bool> TryReserveAsync(User? user, IPAddress? address, long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        var ip = RegistrationBlocks.NormalizeAddress(address);
        if (bytes == 0 || (user == null && ip != null && exemptIps.Contains(ip)))
            return true;

        if (database != null)
        {
            var key = user == null ? $"download-quota:ip:{ip ?? "unknown"}" : UserKey(user.Id);

            try
            {
                var reserved = await database.ScriptEvaluateAsync(ReserveScript, [key],
                    [bytes, GetLimit(user), WeekInSeconds]);
                return (int)reserved == 1;
            }
            catch (Exception e) when (e is RedisException or TimeoutException)
            {
                logger.LogError(e, "Could not reserve download quota");
            }
        }

        throw new HttpResponseException
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Value = "Download quota is temporarily unavailable. Please try again later.",
        };
    }

    private static string UserKey(long id)
    {
        return "download-quota:user:" + id.ToString(CultureInfo.InvariantCulture);
    }
}
