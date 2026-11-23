// <copyright file="UserRecentActivityDTO.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace RevolutionaryWebApp.Shared.Models;

using System;

public sealed class UserRecentActivityDTO
{
    public DateTime CreatedAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
