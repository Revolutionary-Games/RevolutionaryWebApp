namespace ThriveDevCenter.Server.Tests.Fixtures
{
    using System.Collections.Generic;
    using Server.Models;
    using Shared.Models;
    using Shared.Models.Enums;

    public class CIProjectTestDatabaseData
    {
        public static long CIProjectId => 5;
        public static long CIBuildId => 2;

        public static void Seed(ApplicationDbContext database)
        {
            var project = new CiProject()
            {
                Id = CIProjectId,
                Name = "Test Project",
                RepositoryFullName = "test/Repo",
                RepositoryCloneUrl = "https://example.com/repo.git",
                ProjectType = CIProjectType.Github,
                CiBuilds = new List<CiBuild>()
                {
                    new()
                    {
                        CiProjectId = CIProjectId,
                        CiBuildId = CIBuildId,
                        Branch = "master",
                        CommitHash = "abcdef",
                        CommitMessage = "stuff",
                        RemoteRef = "refs/heads/master",
                        IsSafe = true,
                        PreviousCommit = "aabb",
                        Commits = "[]",
                    }
                },
                CiSecrets = new List<CiSecret>()
                {
                    new()
                    {
                        CiProjectId = CIProjectId,
                        CiSecretId = 1,
                        SecretContent = "This is a secret",
                        SecretName = "BUILD_SECRET",
                        UsedForBuildTypes = CISecretType.SafeOnly,
                    }
                }
            };

            database.CiProjects.Add(project);
            database.SaveChanges();
        }
    }
}
