namespace AutomatedUITests.Utilities
{
    using System;
    using System.IO;
    using System.Linq;

    public class SolutionRootFolderFinder
    {
        /// <summary>
        ///   Extracted method from UseSolutionRelativeContentRoot as we also need solution relative web root
        /// </summary>
        /// <returns></returns>
        public static string FindSolutionRootFolder(string solutionName = "*.sln", string baseDirectory = null)
        {
            if (baseDirectory == null)
                baseDirectory = AppContext.BaseDirectory;

            var directoryInfo = new DirectoryInfo(baseDirectory);
            do
            {
                var solutionPath = Directory.EnumerateFiles(directoryInfo.FullName, solutionName).FirstOrDefault();
                if (solutionPath != null)
                {
                    return directoryInfo.FullName;
                }

                directoryInfo = directoryInfo.Parent;
            }
            while (directoryInfo.Parent != null);

            throw new InvalidOperationException("Solution root could not be located");
        }
    }
}
