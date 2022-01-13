namespace ThriveDevCenter.Server.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    public static class DirectoryHelpers
    {
        public static IEnumerable<string> GetFilesAndDirectoriesThatShouldNotExist(string startFolder,
            List<string> filesThatShouldExist)
        {
            var toRemove = new HashSet<string>();
            var wantedFolders = new HashSet<string>();
            var potentiallyRemovedDirectories = new List<string>();

            foreach (var entry in Directory.EnumerateFileSystemEntries(startFolder, "*", SearchOption.AllDirectories))
            {
                if (filesThatShouldExist.Any(f => entry.EndsWith(f)))
                {
                    // We want this, mark the folder this is in as wanted as well
                    var directory = Path.GetDirectoryName(entry);

                    if (directory is { Length: > 0 })
                        wantedFolders.Add(directory);

                    continue;
                }

                if (Directory.Exists(entry))
                {
                    // This is a directory
                    potentiallyRemovedDirectories.Add(entry);
                }
                else
                {
                    // Unwanted file
                    toRemove.Add(entry);
                }
            }

            // Detect folders that didn't have any wanted files in them
            foreach (var directory in potentiallyRemovedDirectories)
            {
                if (wantedFolders.Any(f => f.StartsWith(directory)))
                    continue;

                toRemove.Add(directory);
            }

            // Sort longest paths first to make deletion simpler
            return toRemove.OrderByDescending(p => p.Length).ThenBy(p => p, StringComparer.Ordinal);
        }
    }
}
