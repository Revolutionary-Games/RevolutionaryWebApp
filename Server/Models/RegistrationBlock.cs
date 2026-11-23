// <copyright file="RegistrationBlock.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

[Index(nameof(NormalizedEmail))]
[Index(nameof(IPAddress))]
public class RegistrationBlock
{
    public long Id { get; set; }

    // A block can be either an email or IP address

    [MaxLength(320)]
    public string? Email { get; set; }

    [MaxLength(320)]
    public string? NormalizedEmail { get; set; }

    [MaxLength(45)]
    public string? IPAddress { get; set; }

    public long? TargetUserId { get; set; }
    public User? TargetUser { get; set; }

    public long? ModerationActionId { get; set; }
    public PendingUserModeration? ModerationAction { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
