namespace ThriveDevCenter.Shared.Models;

public enum CIJobState
{
    /// <summary>
    ///   Job has been created, but no server is found to run it on
    /// </summary>
    Starting,

    /// <summary>
    ///   A server has been reserved for the job and now it is waiting for the server to become available to run
    /// </summary>
    WaitingForServer,

    /// <summary>
    ///   Job is running on a server
    /// </summary>
    Running,

    /// <summary>
    ///   The job is finished, no further actions are required for it. The result status is now available
    /// </summary>
    Finished,
}
