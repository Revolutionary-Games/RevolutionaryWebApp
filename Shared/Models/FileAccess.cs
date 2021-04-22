namespace ThriveDevCenter.Shared.Models
{
    public enum FileAccess
    {
        Public = 0,
        User,
        Developer,
        OwnerOrAdmin,

        /// <summary>
        ///   Only system access
        /// </summary>
        Nobody
    }
}
