namespace RevolutionaryWebApp.Server.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///   API helper for interacting with Discourse forum software
/// </summary>
public interface IDiscourseAPI
{
    public bool Configured { get; }

    public Task<DiscourseGroupMembers> GetGroupMembers(string name, CancellationToken cancellationToken);

    /// <summary>
    ///   Gets a discourse full user by email
    /// </summary>
    /// <param name="email">The email address</param>
    /// <param name="cancellationToken">Can cancel this request</param>
    /// <param name="includeNonActive">
    ///   If true even suspended users (ie. users that discourse doesn't consider "active") can be returned
    /// </param>
    /// <param name="avoidEmailQuery">
    ///   If true doesn't query emails back from discourse (avoids log spam). Will fail with multiple results.
    /// </param>
    /// <returns>The user or null</returns>
    /// <remarks>
    ///   <para>
    ///     TODO: this API seems a bit slow? so it would be very nice to find an alternative way to get user emails
    ///     as the group members list doesn't include emails
    ///   </para>
    /// </remarks>
    public Task<DiscourseUser?> FindUserByEmail(string email, CancellationToken cancellationToken,
        bool includeNonActive = false, bool avoidEmailQuery = true);

    /// <summary>
    ///   Gets discourse user info. This variant returns way more information than <see cref="FindUserByEmail"/>
    /// </summary>
    public Task<DiscourseSingleUserInfo> UserInfoByName(string username, CancellationToken cancellationToken);

    public Task<DiscourseGroup> GetGroupInfo(string name, CancellationToken cancellationToken);

    public Task AddGroupMembers(DiscourseGroup group, IEnumerable<string> usernames,
        CancellationToken cancellationToken);

    public Task RemoveGroupMembers(DiscourseGroup group, IEnumerable<string> usernames,
        CancellationToken cancellationToken);
}
