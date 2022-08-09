namespace ThriveDevCenter.Shared.Models.Enums;

using System;

[Flags]
public enum GithubEmailReportReceivers
{
    None = 0,
    Committer = 1 << 0,
    Author = 1 << 1,
    All = Committer | Author,
}