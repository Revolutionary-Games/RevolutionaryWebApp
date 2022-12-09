namespace ThriveDevCenter.Server.Utilities;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Utilities;
using SharedBase.Utilities;

public static class LFSProjectTreeBuilder
{
    private const int MaximumLFSHeaderSize = 512;

    public static async Task BuildFileTree(ILocalTempFileLocks tempFiles, ApplicationDbContext database,
        LfsProject project, ILogger logger, CancellationToken cancellationToken)
    {
        var semaphore = tempFiles.GetTempFilePath($"gitFileTrees/{project.Slug}", out string tempPath);

        await semaphore.WaitAsync(TimeSpan.FromMinutes(10), cancellationToken);

        try
        {
            await GitRunHelpers.EnsureRepoIsCloned(project.CloneUrl, tempPath, true, cancellationToken);

            try
            {
                await GitRunHelpers.Checkout(tempPath, project.BranchToBuildFileTreeFor, true, cancellationToken);
            }
            catch (Exception)
            {
                // In case the branch refers to a new branch
                await GitRunHelpers.Fetch(tempPath, true, cancellationToken);
                await GitRunHelpers.Checkout(tempPath, project.BranchToBuildFileTreeFor, true, cancellationToken);
            }

            await GitRunHelpers.Pull(tempPath, true, cancellationToken, true);

            // Skip if commit has not changed
            var newCommit = await GitRunHelpers.GetCurrentCommit(tempPath, cancellationToken);

            if (newCommit == project.FileTreeCommit)
            {
                logger.LogDebug("Commit is still the same ({FileTreeCommit}), skipping tree update " +
                    "for {Id}",
                    project.FileTreeCommit, project.Id);
                return;
            }

            logger.LogInformation("New commit {NewCommit} to build file tree from (previous: {FileTreeCommit}) " +
                "for project {Id}", newCommit, project.FileTreeCommit, project.Id);

            project.FileTreeCommit = newCommit;

            // Make sure we don't have any extra files locally
            await GitRunHelpers.Clean(tempPath, cancellationToken);

            // And then make sure the DB file tree entries are fine
            await UpdateFileTreeForProject(database, tempPath, project, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }

        project.FileTreeUpdated = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpdateFileTreeForProject(ApplicationDbContext database, string folder,
        LfsProject project, CancellationToken cancellationToken)
    {
        // Folders that should exist and how many items they contain
        var foldersThatShouldExist = new Dictionary<string, int>();

        var existingEntries = await database.ProjectGitFiles.Where(f => f.LfsProjectId == project.Id)
            .ToListAsync(cancellationToken);

        var itemsThatShouldExist = new HashSet<ProjectGitFile>();

        // Note that all paths need to start with a "/"

        // Create new files
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            // Skip .git folder
            if (entry.Contains(".git"))
                continue;

            var justRepoPath = entry.Substring(folder.Length);

            if (!justRepoPath.StartsWith('/'))
                throw new Exception("Generated file path doesn't start with a slash");

            var parentFolder = GetParentPath(justRepoPath);

            // Don't create the root folder item
            if (parentFolder != "/")
            {
                foldersThatShouldExist.TryGetValue(parentFolder, out int existingItemCount);

                foldersThatShouldExist[parentFolder] = existingItemCount + 1;
            }

            // Only add folders to their parent folder's count of items
            if (Directory.Exists(entry))
                continue;

            var name = Path.GetFileName(justRepoPath);

            // Detect if this is an LFS file
            int size = (int)new FileInfo(entry).Length;
            string? oid = null;

            var (detectedOid, detectedSize) = await DetectLFSFile(entry, cancellationToken);

            if (detectedOid != null)
            {
                oid = detectedOid;
                size = detectedSize!.Value;
            }

            // For files there needs to be an entry
            var existing = existingEntries.FirstOrDefault(f =>
                f.FType == FileType.File && f.Name == name && f.Path == justRepoPath);

            if (existing != null)
            {
                existing.Size = size;
                existing.LfsOid = oid;

                itemsThatShouldExist.Add(existing);
            }
            else
            {
                await database.ProjectGitFiles.AddAsync(new ProjectGitFile
                {
                    FType = FileType.File,
                    LfsProjectId = project.Id,
                    Name = name,
                    Path = parentFolder,
                    Size = size,
                    LfsOid = oid,
                }, cancellationToken);
            }
        }

        // Create / update folders
        foreach (var (processedFolder, size) in foldersThatShouldExist)
        {
            var name = Path.GetFileName(processedFolder);
            var path = GetParentPath(processedFolder);

            var existing =
                existingEntries.FirstOrDefault(f => f.FType == FileType.Folder && f.Name == name && f.Path == path);

            if (existing != null)
            {
                existing.Size = size;

                itemsThatShouldExist.Add(existing);
            }
            else
            {
                await database.ProjectGitFiles.AddAsync(new ProjectGitFile
                {
                    FType = FileType.Folder,
                    LfsProjectId = project.Id,
                    Name = name,
                    Path = path,
                    Size = size,
                }, cancellationToken);
            }
        }

        // Delete files and folders that shouldn't exist
        database.ProjectGitFiles.RemoveRange(existingEntries.Except(itemsThatShouldExist));
    }

    private static string GetParentPath(string path)
    {
        var result = PathParser.GetParentPath(path);

        // We specifically want to have a starting '/' in paths
        if (string.IsNullOrEmpty(result))
            return "/";

        return result;
    }

    private static async Task<(string? Oid, int? Size)> DetectLFSFile(string path,
        CancellationToken cancellationToken)
    {
        bool identifiedAsLFS = false;
        string? oid = null;
        int? size = null;

        string[]? lines;

        try
        {
            var reader = File.OpenText(path);

            var buffer = new char[MaximumLFSHeaderSize];

            await reader.ReadAsync(buffer, cancellationToken);

            var data = new string(buffer);

            lines = data.Split('\n');
        }
        catch (Exception)
        {
            // If reading fails, assume it is binary or something else
            return (null, null);
        }

        foreach (var line in lines)
        {
            var split = line.Split(' ', 2);

            if (split.Length != 2)
                continue;

            var lineType = split[0].Trim();
            var data = split[1].Trim();

            if (identifiedAsLFS)
            {
                if (lineType == "oid")
                {
                    var oidParts = data.Split(':', 2);

                    if (oidParts.Length == 2 && oidParts[0] == "sha256")
                    {
                        oid = oidParts[1];
                    }
                }
                else if (lineType == "size")
                {
                    size = Convert.ToInt32(data);

                    // Size is usually (probably always) after the oid, so we can processing once we see the size
                    if (oid != null)
                        break;
                }
            }
            else
            {
                if (lineType == "version" && data.Contains("git-lfs"))
                    identifiedAsLFS = true;
            }
        }

        if (oid == null || size == null)
            return (null, null);

        return (oid, size);
    }
}
