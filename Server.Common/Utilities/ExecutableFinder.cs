namespace ThriveDevCenter.Server.Common.Utilities
{
    using System;
    using System.IO;

    public static class ExecutableFinder
    {
        /// <summary>
        ///   Tries to find command / executable in path
        /// </summary>
        /// <param name="commandName">The name of the executable</param>
        /// <returns>Full path to the command or null</returns>
        /// <remarks>
        ///   <para>
        ///     This approach has been ported over from RubySetupSystem
        ///   </para>
        /// </remarks>
        public static string? Which(string commandName)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32Windows)
            {
                if (commandName.EndsWith(".exe"))
                {
                    commandName = commandName.Substring(0, commandName.Length - ".exe".Length);
                }
            }

            var extensions = PathExtensions();

            foreach (var path in SystemPath())
            {
                foreach (var extension in extensions)
                {
                    var fullPath = Path.Join(path, $"{commandName}{extension}");

                    if (File.Exists(fullPath))
                    {
                        var attributes = File.GetAttributes(fullPath);
                        // TODO: there used to be executable flag check here but apparently C# doesn't have that
                        // So that is skipped, so this can find something that isn't an executable that is in PATH
                        if(!attributes.HasFlag(FileAttributes.Directory))
                            return fullPath;
                    }
                }
            }

            return null;
        }

        public static string[] SystemPath()
        {
            return Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ??
                throw new Exception("PATH environment variable is missing");
        }

        public static string[] PathExtensions()
        {
            return (Environment.GetEnvironmentVariable("PATHEXT") ?? "").Split(';');
        }
    }
}
