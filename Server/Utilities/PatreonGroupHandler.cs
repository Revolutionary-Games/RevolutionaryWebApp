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
        public static async Task<bool> HandlePatreonPledgeObject(PatreonObjectData pledge, PatreonObjectData user,
            string rewardId, NotificationsEnabledDb database, IBackgroundJobClient jobClient)
        {
            var pledgeCents = Convert.ToInt32(pledge.Attributes["amount_cents"]);

            bool declined = pledge.Attributes.ContainsKey("declined_since") &&
                !string.IsNullOrEmpty(pledge.Attributes["declined_since"]);

            var email = user.Attributes["email"];

            var patron = await database.Patrons.AsQueryable().FirstOrDefaultAsync(p => p.Email == email);

            var username = user.Attributes["full_name"];

            if (user.Attributes.TryGetValue("vanity", out string vanity) && !string.IsNullOrEmpty(vanity))
            {
                username = vanity;
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
                        Message = "A patron is now in declined state. Setting as suspended"
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
                    Message = "A patron has changed their reward or name"
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
    }
}
