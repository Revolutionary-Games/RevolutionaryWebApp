namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Controllers;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;

    public static class SSOSuspendHandler
    {
        /// <summary>
        ///   Checks (and applies) suspension for an use using SSO
        /// </summary>
        public static async Task<bool> CheckUser(User user, ApplicationDbContext database,
            CommunityForumAPI communityAPI, DevForumAPI devForumAPI, ILogger logger,
            CancellationToken cancellationToken)
        {
            if (user.Local)
                return false;

            bool shouldBeSuspended = true;
            string reason = "SSO user no longer valid for login";

            switch (user.SsoSource)
            {
                case LoginController.SsoTypePatreon:
                {
                    // TODO: email alias handling
                    var patron =
                        await database.Patrons.FirstOrDefaultAsync(p => p.Email == user.Email, cancellationToken);

                    if (patron == null)
                    {
                        reason = "No longer a patron";
                    }
                    else if (patron.Suspended == true)
                    {
                        reason = patron.SuspendedReason ?? "Suspended as a patron but no further reason given";
                    }
                    else
                    {
                        shouldBeSuspended = false;
                    }

                    break;
                }
                case LoginController.SsoTypeCommunityForum:
                {
                    if (!communityAPI.Configured)
                    {
                        logger.LogWarning(
                            "Can't check SSO user from community forum because API for that is unconfigured");
                        return false;
                    }

                    var discourseUser = await communityAPI.FindUserByEmail(user.Email, cancellationToken);

                    if (discourseUser != null)
                    {
                        var fullInfo = await communityAPI.UserInfoByName(discourseUser.Username, cancellationToken);

                        if (fullInfo == null)
                            throw new Exception("Second retrieve by name from community discourse failed");

                        bool found = false;

                        foreach (var group in fullInfo.User.Groups)
                        {
                            if (group.Name == PatreonGroupHandler.CommunityDevBuildGroup ||
                                group.Name == PatreonGroupHandler.CommunityVIPGroup)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (found)
                        {
                            shouldBeSuspended = false;
                        }
                        else
                        {
                            reason = "No longer part of required forum group";
                        }
                    }

                    break;
                }

                case LoginController.SsoTypeDevForum:
                {
                    if (!devForumAPI.Configured)
                    {
                        logger.LogWarning(
                            "Can't check SSO user from dev forum because API for that is unconfigured");
                        return false;
                    }

                    var discourseUser = await devForumAPI.FindUserByEmail(user.Email, cancellationToken);

                    if (discourseUser != null)
                        shouldBeSuspended = false;

                    break;
                }

                default:
                    throw new ArgumentException($"Unknown SSO source in user: {user.SsoSource}");
            }

            if (user.Suspended == shouldBeSuspended)
                return false;

            // Need to change suspend status

            // Don't unsuspend if user was suspended manually
            if (user.Suspended == true && user.SuspendedManually != true)
            {
                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = "Unsuspended user from sso sources",
                    TargetUserId = user.Id
                }, cancellationToken);

                user.Suspended = false;
            }
            else if (user.Suspended != true)
            {
                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message =
                        $"Suspending user due to sso login ({user.SsoSource}) no longer being valid for this user",
                    TargetUserId = user.Id
                }, cancellationToken);

                user.Suspended = true;
                user.SuspendedReason = $"Used login option is no longer valid {reason}";
            }

            return true;
        }
    }
}
