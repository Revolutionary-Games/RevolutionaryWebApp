namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Jobs;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using Services;

    public static class PatreonGroupHandler
    {
        public const string CommunityDevBuildGroup = "Supporter";
        public const string CommunityVIPGroup = "VIP_Supporter";

        public enum RewardGroup
        {
            None,
            DevBuild,
            VIP
        }

        public static async Task<bool> HandlePatreonPledgeObject(PatreonObjectData pledge, PatreonObjectData user,
            string rewardId, NotificationsEnabledDb database, IBackgroundJobClient jobClient)
        {
            if (pledge.Attributes.AmountCents == null || user.Attributes.Email == null)
                throw new Exception("Invalid patron API object, missing key properties");

            var pledgeCents = pledge.Attributes.AmountCents.Value;

            bool declined = !string.IsNullOrEmpty(pledge.Attributes.DeclinedSince);

            var email = user.Attributes.Email?.Trim();

            if (string.IsNullOrEmpty(email))
                throw new Exception("Patron object has null email");

            var patron = await database.Patrons.FirstOrDefaultAsync(p => p.Email == email);

            var username = user.Attributes.Vanity;

            if (string.IsNullOrWhiteSpace(username))
                username = user.Attributes.FullName;

            if (string.IsNullOrWhiteSpace(username))
            {
                // TODO: to resolve already used name conflicts the Id could be appended here
                username = user.Attributes.FirstName;
            }

            // Ensure no trailing spaces in patron names
            username = username?.Trim();

            if (string.IsNullOrWhiteSpace(username))
            {
                // Fallback to using the id if everything failed...
                username = $"Patron {user.Id}";
            }

            if (patron == null)
            {
                if (!declined)
                {
                    await database.LogEntries.AddAsync(new LogEntry()
                    {
                        Message = $"We have a new patron: {username}"
                    });

                    await database.Patrons.AddAsync(new Patron()
                    {
                        Username = username,
                        Email = email,
                        PledgeAmountCents = pledgeCents,
                        RewardId = rewardId,
                        Marked = true
                    });

                    return true;
                }

                return false;
            }

            patron.Marked = true;

            bool changes = false;
            bool reapplySuspension = false;

            if (declined)
            {
                if (patron.Suspended != true)
                {
                    await database.LogEntries.AddAsync(new LogEntry()
                    {
                        Message = $"A patron ({patron.Id}) is now in declined state. Setting as suspended",
                    });

                    patron.Suspended = true;
                    patron.SuspendedReason = "Payment failed on Patreon";
                    reapplySuspension = true;

                    changes = true;
                }
            }
            else if (patron.RewardId != rewardId || patron.Username != username)
            {
                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = $"A patron ({patron.Id}) has changed their reward or name",
                });

                patron.RewardId = rewardId;
                patron.PledgeAmountCents = pledgeCents;
                patron.Username = username;
                patron.Suspended = false;
                reapplySuspension = true;

                changes = true;
            }
            else if (patron.Suspended == true)
            {
                patron.Suspended = false;
                reapplySuspension = true;

                changes = true;
            }

            if (reapplySuspension)
            {
                // Need to wait for this job as the changes aren't saved immediately
                jobClient.Schedule<CheckSSOUserSuspensionJob>(x => x.Execute(patron.Email, CancellationToken.None),
                    TimeSpan.FromSeconds(30));
            }

            return changes;
        }

        public static RewardGroup ShouldBeInGroupForPatron(Patron patron, PatreonSettings settings)
        {
            if (settings == null)
                throw new ArgumentException("patreon settings is null");

            if (patron == null || patron.Suspended == true)
                return RewardGroup.None;

            if (patron.RewardId == settings.VipRewardId)
                return RewardGroup.VIP;

            if (patron.RewardId == settings.DevbuildsRewardId)
                return RewardGroup.DevBuild;

            return RewardGroup.None;
        }
    }
}
