namespace ThriveDevCenter.Shared.Models;

using DevCenterCommunication.Models;

public class DevBuildDTO : DevBuildLauncherDTO
{
}

public enum DevBuildSearchType
{
    BOTD,
    NonAnonymous,
    Anonymous,
}
