namespace ThriveDevCenter.Shared.Models;

public enum RecordAccessLevel
{
    /// <summary>
    ///   Least privileged access. Not necessarily to the public. Might be nice to come up with a better name
    /// </summary>
    Public,

    Private,

    /// <summary>
    ///   Access to all parts of a record, things even users can't see about themselves
    /// </summary>
    Admin,
}