namespace RevolutionaryWebApp.Server.Tests.Fixtures;

using System.Collections.Generic;
using DevCenterCommunication.Models.Enums;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;

public class CIProjectTestDatabaseData
{
    public static long CIProjectId => 5;
    public static long CIBuildId => 2;
    public static string TestImageStoragePath => "test/image/path.tar.xz";

    public static void Seed(ApplicationDbContext database)
    {
        var project = new CiProject
        {
            Id = CIProjectId,
            Name = "Test Project",
            RepositoryFullName = "test/Repo",
            RepositoryCloneUrl = "https://example.com/repo.git",
            ProjectType = CIProjectType.Github,
            CiBuilds = new List<CiBuild>
            {
                new()
                {
                    CiProjectId = CIProjectId,
                    CiBuildId = CIBuildId,
                    Branch = "master",
                    CommitHash = "deadbeef",
                    CommitMessage = "stuff",
                    RemoteRef = "refs/heads/master",
                    IsSafe = true,
                    PreviousCommit = "aabb",
                    Commits = "[]",
                },
            },
            CiSecrets = new List<CiSecret>
            {
                new()
                {
                    CiProjectId = CIProjectId,
                    CiSecretId = 1,
                    SecretContent = "This is a secret",
                    SecretName = "BUILD_SECRET",
                    UsedForBuildTypes = CISecretType.SafeOnly,
                },
            },
        };

        database.CiProjects.Add(project);

        // Build image used by the job(s)
        var imageFile = new StorageFile
        {
            StoragePath = TestImageStoragePath,
            Size = 123,
            Uploading = false,
        };

        var imageVersion = new StorageItemVersion
        {
            Uploading = false,
            StorageFile = imageFile,
        };
        imageFile.StorageItemVersions = new List<StorageItemVersion> { imageVersion };

        var ciImageFile = new StorageItem
        {
            Name = new CiJob { Image = "test:v1" }.GetImageFileName(),
            Ftype = FileType.File,
            WriteAccess = FileAccess.Nobody,
            Special = true,
            StorageItemVersions = new List<StorageItemVersion>
            {
                imageVersion,
            },
        };
        imageVersion.StorageItem = ciImageFile;

        var testFolder = new StorageItem
        {
            Name = "test",
            Ftype = FileType.Folder,
            Children = new List<StorageItem>
            {
                ciImageFile,
            },
        };
        ciImageFile.Parent = testFolder;

        var imagesFolder = new StorageItem
        {
            Name = "Images",
            Ftype = FileType.Folder,
            Children = new List<StorageItem>
            {
                testFolder,
            },
        };
        testFolder.Parent = imagesFolder;

        var ciFolder = new StorageItem
        {
            Name = "CI",
            AllowParentless = true,
            Ftype = FileType.Folder,
            Children = new List<StorageItem>
            {
                imagesFolder,
            },
        };
        imagesFolder.Parent = ciFolder;

        database.StorageFiles.Add(imageFile);
        database.StorageItemVersions.Add(imageVersion);
        database.StorageItems.Add(ciImageFile);
        database.StorageItems.Add(ciImageFile);
        database.StorageItems.Add(testFolder);
        database.StorageItems.Add(imagesFolder);
        database.StorageItems.Add(ciFolder);

        database.SaveChanges();
    }
}
