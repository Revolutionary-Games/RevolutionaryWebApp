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

    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(320)]
    public string NormalizedEmail { get; set; } = string.Empty;

    [MaxLength(45)]
    public string? IPAddress { get; set; }

    public long? TargetUserId { get; set; }
    public User? TargetUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
