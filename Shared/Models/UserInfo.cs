namespace ThriveDevCenter.Shared.Models
{
    public class UserInfo : ClientSideTimedModel
    {
        public string Name { get; set; }
        public string Email { get; set; }

        public bool Local { get; set; }
        public string SsoSource { get; set; }

        public bool Developer { get; set; }
        public bool Admin { get; set; }

        /// <summary>
        ///   Precomputed access level from the server
        /// </summary>
        public UserAccessLevel AccessLevel { get; set; }

        public bool HasApiToken { get; set; }
        public bool HasLfsToken { get; set; }
        public int TotalLauncherLinks { get; set; }

        public bool Suspended { get; set; }
        public string SuspendedReason { get; set; }
        public bool SuspendedManually { get; set; }

        public int SessionVersion { get; set; }


    }
}
