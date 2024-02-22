﻿namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Threading.Tasks;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Utilities;

[Index(nameof(StorageFileId))]
[Index(nameof(StorageItemId), nameof(Version), IsUnique = true)]
public class StorageItemVersion : UpdateableModel, ISoftDeletable
{
    [AllowSortingBy]
    public int Version { get; set; } = 1;

    public long StorageItemId { get; set; }
    public StorageItem? StorageItem { get; set; }

    public long StorageFileId { get; set; }
    public StorageFile? StorageFile { get; set; }

    [AllowSortingBy]
    public bool Keep { get; set; }

    [AllowSortingBy]
    public bool Protected { get; set; }

    [AllowSortingBy]
    public bool Uploading { get; set; } = true;

    public long? UploadedById { get; set; }
    public User? UploadedBy { get; set; }

    public bool Deleted { get; set; }

    public async Task<string> ComputeStoragePath(ApplicationDbContext database)
    {
        if (StorageItem == null)
            throw new NotLoadedModelNavigationException();

        string parentPath = string.Empty;

        if (StorageItem.ParentId != null)
        {
            var parent = StorageItem.Parent;

            if (parent == null)
            {
                parent = await database.StorageItems.FindAsync(StorageItem.ParentId);

                if (parent == null)
                    throw new NullReferenceException("failed to get the StorageItem parent for path calculation");
            }

            parentPath = await parent.ComputeStoragePath(database) + '/';
        }

        return $"{parentPath}{Version}/{StorageItem.Name}";
    }

    public async Task<StorageFile> CreateStorageFile(ApplicationDbContext database, DateTime uploadExpiresAt,
        long size)
    {
        var file = new StorageFile
        {
            StoragePath = await ComputeStoragePath(database),
            Size = size,
            Uploading = true,
            UploadExpires = uploadExpiresAt + TimeSpan.FromSeconds(1),
        };

        StorageFile = file;

        await database.StorageFiles.AddAsync(file);
        return file;
    }

    public StorageItemVersionInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            Version = Version,
            Keep = Keep,
            Protected = Protected,
            Uploading = Uploading,
            Size = StorageFile?.Size,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            UploadedById = UploadedById,
            Deleted = Deleted,
        };
    }
}
