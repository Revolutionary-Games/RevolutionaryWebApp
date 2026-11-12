namespace RevolutionaryWebApp.Shared.Models;

using System;

public class TwoFactorStatusDTO
{
    public bool Enabled { get; set; }

    public int BackupCodesRemaining { get; set; }

    public DateTime? RecoveryAvailableAt { get; set; }
}
