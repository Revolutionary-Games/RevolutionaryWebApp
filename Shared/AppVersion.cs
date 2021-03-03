namespace ThriveDevCenter.Shared
{
    /// <summary>
    ///   Holds the current version of the app, to detect mismatch between the client and the server.
    ///   Increment these numbers when the signalr definitions change or the APIs change
    /// </summary>
    public static class AppVersion
    {
        public static int Major => 1;
        public static int Minor => 5;
    }
}
