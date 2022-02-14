using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Authorization;
    using Filters;
    using Hangfire;
    using Jobs;
    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared;
    using Shared.Forms;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/devbuild")]
    public class DevBuildUploadController : Controller
    {
        private const string DevBuildUploadProtectionPurposeString = "DevBuild.Upload.v1";

        /// <summary>
        ///   Platforms which we accept devbuilds for
        /// </summary>
        private static readonly List<string> AllowedDevBuildPlatforms = new()
        {
            "Linux/X11",
            "Windows Desktop"
        };

        private readonly ILogger<DevBuildUploadController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;
        private readonly GeneralRemoteStorage remoteStorage;
        private readonly IDataProtector dataProtector;

        public DevBuildUploadController(ILogger<DevBuildUploadController> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient, GeneralRemoteStorage remoteStorage,
            IDataProtectionProvider dataProtectionProvider)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
            this.remoteStorage = remoteStorage;
            dataProtector = dataProtectionProvider.CreateProtector(DevBuildUploadProtectionPurposeString);
        }

        /// <summary>
        ///   Checks if the server wants the specified devbuild
        /// </summary>
        [HttpPost("offer_devbuild")]
        public async Task<ActionResult<DevBuildOfferResult>> OfferBuild(
            [Required] [FromBody] DevBuildOfferRequest request)
        {
            if (!AllowedDevBuildPlatforms.Contains(request.BuildPlatform))
                return BadRequest("Invalid DevBuild platform");

            var failResult = GetAccessStatus(out var anonymous);
            if (failResult != null)
                return failResult;

            IQueryable<DevBuild> query;
            if (anonymous)
            {
                query =
                    database.DevBuilds.Where(b =>
                        b.BuildHash == request.BuildHash && b.Platform == request.BuildPlatform);
            }
            else
            {
                query =
                    database.DevBuilds.Where(b => b.BuildHash == request.BuildHash &&
                        b.Platform == request.BuildPlatform && b.Anonymous == false);
            }

            var existing = await query.Include(b => b.StorageItem).FirstOrDefaultAsync();

            bool upload = true;

            if (existing != null)
            {
                // Separate lookup is used here to avoid a collection scan from:
                // StorageItemVersions = e.StorageItem.StorageItemVersions.OrderByDescending(v => v.Version).Take(1)

                upload = !await existing.IsUploaded(database);
            }

            return new DevBuildOfferResult { Upload = upload };
        }

        /// <summary>
        ///   Checks if the server wants any of the specified dehydrated objects
        /// </summary>
        [HttpPost("offer_objects")]
        public async Task<ActionResult<DevObjectOfferResult>> OfferObjects(
            [Required] [FromBody] ObjectOfferRequest request)
        {
            var failResult = GetAccessStatus(out _);
            if (failResult != null)
                return failResult;

            var result = new DevObjectOfferResult();

            foreach (var obj in request.Objects)
            {
                var existing = await database.DehydratedObjects.Include(d => d.StorageItem)
                    .FirstOrDefaultAsync(d => d.Sha3 == obj.Sha3);

                if (existing != null)
                {
                    if (existing.StorageItem == null)
                        throw new NotLoadedModelNavigationException();

                    var version = await existing.StorageItem.GetHighestVersion(database);
                    if (version is { Uploading: false })
                        continue;
                }

                result.Upload.Add(obj.Sha3);
            }

            return result;
        }

        /// <summary>
        ///   Starts upload of a devbuild. The required objects need to be already uploaded
        /// </summary>
        [HttpPost("upload_devbuild")]
        public async Task<ActionResult<DevBuildUploadResult>> StartDevBuildUpload(
            [Required] [FromBody] DevBuildUploadRequest request)
        {
            if (!AllowedDevBuildPlatforms.Contains(request.BuildPlatform))
                return BadRequest("Invalid DevBuild platform");

            var failResult = GetAccessStatus(out var anonymous);
            if (failResult != null)
                return failResult;

            if (!remoteStorage.Configured)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = "Remote storage is not configured"
                };
            }

            if (request.BuildHash.Contains('/'))
                return BadRequest("The hash contains an invalid character");

            var existing = await database.DevBuilds.Include(d => d.StorageItem).Include(d => d.DehydratedObjects)
                .FirstOrDefaultAsync(d => d.BuildHash == request.BuildHash && d.Platform == request.BuildPlatform);

            if (anonymous)
            {
                if (existing != null)
                {
                    return Unauthorized("Can't upload over an existing build without an access key");
                }
            }
            else if (existing != null)
            {
                // Non-anonymous upload can overwrite an anonymous upload
                if (existing.Anonymous)
                {
                    logger.LogInformation("Anonymous devbuild ({BuildHash}) upload is being overwritten",
                        existing.BuildHash);

                    existing.Anonymous = false;

                    // Clear verified status as the files are being replaced
                    existing.Verified = false;
                    existing.VerifiedById = null;
                }
                else if (await existing.IsUploaded(database))
                {
                    // This is OK result so that the CI doesn't fail in case it ends up with a duplicate build
                    return Ok("Can't upload a new version of an existing build");
                }
            }

            var folder = await StorageItem.GetDevBuildBuildsFolder(database);

            if (folder == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = "Storage folder is missing"
                };
            }

            if (existing == null)
            {
                var sanitizedPlatform = request.BuildPlatform.Replace("/", "");

                var fileName = $"{request.BuildHash}_{sanitizedPlatform}.7z";

                var storageItem =
                    await database.StorageItems.FirstOrDefaultAsync(i => i.Name == fileName && i.Parent == folder);

                if (storageItem == null)
                {
                    storageItem = new StorageItem()
                    {
                        Name = fileName,
                        Parent = folder,
                        Ftype = FileType.File,
                        Special = true,
                        ReadAccess = FileAccess.User,
                        WriteAccess = FileAccess.Nobody
                    };

                    await database.StorageItems.AddAsync(storageItem);
                }

                existing = new DevBuild()
                {
                    BuildHash = request.BuildHash,
                    Platform = request.BuildPlatform,
                    Branch = request.BuildBranch,
                    StorageItem = storageItem,
                    Anonymous = anonymous,

                    // This will get overwritten when the actual file upload finishes
                    // (if it was for the latest version)
                    BuildZipHash = "notUploaded",

                    // TODO: put this logic in somewhere more obvious
                    Important = !anonymous && (request.BuildBranch == "master" || request.BuildBranch == "main")
                };

                await database.DevBuilds.AddAsync(existing);
                await database.SaveChangesAsync();

                jobClient.Enqueue<CountFolderItemsJob>((x) => x.Execute(folder.Id, CancellationToken.None));
            }

            // Apply objects

            var dehydrated = await request.RequiredDehydratedObjects.ToAsyncEnumerable().SelectAwait(hash =>
                    new ValueTask<DehydratedObject?>(
                        database.DehydratedObjects.FirstOrDefaultAsync(d => d.Sha3 == hash)))
                .ToListAsync();

            if (dehydrated.Any(item => item == null))
                return BadRequest("One or more dehydrated object hashes doesn't exist");

            foreach (var dehydratedObject in dehydrated)
                existing.DehydratedObjects.Add(dehydratedObject!);

            if (existing.StorageItem == null)
                throw new NotLoadedModelNavigationException();

            // Upload a version of the build
            var version = await existing.StorageItem.CreateNextVersion(database);
            var file = await version.CreateStorageFile(database,
                DateTime.UtcNow + AppInfo.RemoteStorageUploadExpireTime,
                request.BuildSize);

            await database.SaveChangesAsync();

            logger.LogInformation("Upload of ({BuildHash} on {Platform}) starting from {RemoteIpAddress}",
                existing.BuildHash, existing.Platform, HttpContext.Connection.RemoteIpAddress);

            if (file.Size == null)
            {
                // Should not happen, can be removed once Size is no longer nullable
                throw new Exception("this shouldn't happen");
            }

            return new DevBuildUploadResult(
                remoteStorage.CreatePresignedUploadURL(file.UploadPath,
                    AppInfo.RemoteStorageUploadExpireTime),
                new StorageUploadVerifyToken(dataProtector, file.UploadPath, file.StoragePath,
                    file.Size.Value,
                    file.Id, existing.Id, null, request.BuildZipHash).ToString()
            );
        }

        /// <summary>
        ///   Starts upload of the specified objects
        /// </summary>
        [HttpPost("upload_objects")]
        public async Task<ActionResult<DehydratedUploadResult>> StartObjectUpload(
            [Required] [FromBody] DehydratedUploadRequest request)
        {
            var failResult = GetAccessStatus(out var anonymous);
            if (failResult != null)
                return failResult;

            if (!remoteStorage.Configured)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = "Remote storage is not configured"
                };
            }

            var result = new DehydratedUploadResult();

            var folder = await StorageItem.GetDehydratedFolder(database);

            if (folder == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = "Storage folder is missing"
                };
            }

            bool addedItems = false;

            if (request.Objects.Any(o => o.Sha3.Contains('/')))
                return BadRequest("A hash contains an invalid character");

            var expiresAt = DateTime.UtcNow + AppInfo.RemoteStorageUploadExpireTime;

            foreach (var obj in request.Objects)
            {
                var dehydrated = await database.DehydratedObjects.Include(d => d.StorageItem)
                    .FirstOrDefaultAsync(d => d.Sha3 == obj.Sha3);

                if (dehydrated != null && await dehydrated.IsUploaded(database))
                {
                    continue;
                }

                // Needs to be uploaded (wasn't uploaded already)

                if (anonymous)
                    logger.LogInformation("Anonymous upload of dehydrated object: {Sha3}", obj.Sha3);

                if (dehydrated == null)
                {
                    var storageItem = new StorageItem()
                    {
                        Name = $"{obj.Sha3}.gz",
                        Parent = folder,
                        Ftype = FileType.File,
                        Special = true,
                        ReadAccess = FileAccess.User,
                        WriteAccess = FileAccess.Nobody
                    };

                    dehydrated = new DehydratedObject()
                    {
                        Sha3 = obj.Sha3,
                        StorageItem = storageItem
                    };

                    await database.StorageItems.AddAsync(storageItem);
                    await database.DehydratedObjects.AddAsync(dehydrated);

                    addedItems = true;
                }

                if (dehydrated.StorageItem == null)
                    throw new NotLoadedModelNavigationException();

                var version = await dehydrated.StorageItem.CreateNextVersion(database);
                var file = await version.CreateStorageFile(database, expiresAt, obj.Size);

                if (file.Size == null)
                {
                    // Should not happen, can be removed once Size is no longer nullable
                    throw new Exception("this shouldn't happen");
                }

                logger.LogInformation("Upload of a dehydrated object ({Sha3}) starting from {RemoteIpAddress}",
                    obj.Sha3, HttpContext.Connection.RemoteIpAddress);

                result.Upload.Add(new DehydratedUploadResult.ObjectToUpload(
                    obj.Sha3,
                    remoteStorage.CreatePresignedUploadURL(file.UploadPath,
                        AppInfo.RemoteStorageUploadExpireTime),
                    new StorageUploadVerifyToken(dataProtector, file.UploadPath, file.StoragePath,
                        file.Size.Value,
                        file.Id, null, obj.Sha3, null).ToString()
                ));
            }

            await database.SaveChangesAsync();

            if (addedItems)
                jobClient.Enqueue<CountFolderItemsJob>((x) => x.Execute(folder.Id, CancellationToken.None));

            return result;
        }

        /// <summary>
        ///   Report finished devbuild / object upload
        /// </summary>
        [HttpPost("finish")]
        public async Task<IActionResult> FinishUpload([Required] [FromBody] TokenForm request)
        {
            // TODO: could include the key info in the token
            var failResult = GetAccessStatus(out var anonymous);
            if (failResult != null)
                return failResult;

            if (!remoteStorage.Configured)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = "Remote storage is not configured"
                };
            }

            var decodedToken = StorageUploadVerifyToken.TryToLoadFromString(dataProtector, request.Token);

            if (decodedToken == null)
                return BadRequest("Invalid finished upload token");

            StorageFile file;
            try
            {
                file = await remoteStorage.HandleFinishedUploadToken(database, decodedToken);
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to check upload token / resulting file: {@E}", e);
                return BadRequest("Failed to verify that uploaded file is valid");
            }

            DevBuild? build = null;

            if (decodedToken.ParentId != null)
            {
                // This is used in the token here to store the devbuild ID
                build = await database.DevBuilds.FindAsync(decodedToken.ParentId.Value);

                if (build == null)
                    return BadRequest("No build found with id in token");

                if (anonymous && !build.Anonymous)
                {
                    return BadRequest("Can't upload over a non-anonymous build without a key");
                }
            }

            // Check that item hash matches
            string actualHash;
            string expectedHash;
            try
            {
                if (string.IsNullOrEmpty(decodedToken.UnGzippedHash))
                {
                    expectedHash = decodedToken.PlainFileHash ??
                        throw new Exception("plain file hash is needed if gzipped hash is not provided");
                    actualHash = await remoteStorage.ComputeSha3OfObject(file.StoragePath);
                }
                else
                {
                    expectedHash = decodedToken.UnGzippedHash;
                    actualHash = await remoteStorage.ComputeSha3UnZippedObject(file.StoragePath);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("Hash calculation failed: {@E}", e);
                return BadRequest("Failed to calculate uploaded file hash");
            }

            if (actualHash != expectedHash)
            {
                // Delete the uploaded file to not leave it hanging around
                await remoteStorage.DeleteObject(file.StoragePath);
                return BadRequest("Uploaded file hash doesn't match the expected value");
            }

            if (build != null)
            {
                // Update build hash if this is the latest version
                var highestVersion = await database.StorageItemVersions
                    .Where(s => s.StorageItemId == build.StorageItemId).MaxAsync(s => s.Version);

                if (file.StorageItemVersions.Max(s => s.Version) >= highestVersion)
                {
                    if (string.IsNullOrEmpty(decodedToken.PlainFileHash))
                    {
                        // TODO: make sure that this is now correct. This print was happening all the time
                        // So I changed this to only be printed in the error case -hhyyrylainen
                        logger.LogError("Can't set hash for build {Id} as it wasn't correctly in token", build.Id);
                        build.BuildZipHash = "missing hash when uploading";
                    }
                    else
                    {
                        build.BuildZipHash = decodedToken.PlainFileHash;
                    }
                }
                else
                {
                    logger.LogInformation("Uploaded a non-highest version item, not updating hash");
                }
            }

            await remoteStorage.PerformFileUploadSuccessActions(file, database);
            await database.SaveChangesAsync();

            logger.LogInformation("DevBuild item ({StoragePath}) is now uploaded", file.StoragePath);

            return Ok();
        }

        /// <summary>
        ///   Checks the access to the devbuild endpoint
        /// </summary>
        /// <param name="anonymous">Whether this is an anonymous (unsafe) upload or not</param>
        /// <returns>A failure result. If not null the main action should return this</returns>
        [NonAction]
        private ActionResult? GetAccessStatus(out bool anonymous)
        {
            switch (HttpContext.HasAuthenticatedAccessKeyExtended(AccessKeyType.DevBuilds))
            {
                case HttpContextAuthorizationExtensions.AuthenticationResult.NoUser:
                    anonymous = true;
                    return null;
                case HttpContextAuthorizationExtensions.AuthenticationResult.NoAccess:
                    anonymous = true;
                    return Forbid();
                case HttpContextAuthorizationExtensions.AuthenticationResult.Success:
                    anonymous = false;
                    return null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public class DevBuildOfferRequest
    {
        [Required]
        [JsonPropertyName("build_hash")]
        public string BuildHash { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("build_platform")]
        public string BuildPlatform { get; set; } = string.Empty;
    }

    public class DevBuildOfferResult
    {
        [JsonPropertyName("upload")]
        public bool Upload { get; set; }
    }

    public class ObjectOfferRequest
    {
        [Required]
        [MaxLength(AppInfo.MaxDehydratedObjectsPerOffer)]
        public List<DehydratedObjectRequest> Objects { get; set; } = new();
    }

    public class DehydratedObjectIdentification
    {
        public DehydratedObjectIdentification(string sha3)
        {
            Sha3 = sha3;
        }

        [JsonPropertyName("sha3")]
        [Required]
        [MinLength(5)]
        [MaxLength(100)]
        public string Sha3 { get; set; }
    }

    public class DehydratedObjectRequest : DehydratedObjectIdentification
    {
        public DehydratedObjectRequest(string sha3, int size) : base(sha3)
        {
            Size = size;
        }

        [Required]
        [Range(1, AppInfo.MaxDehydratedUploadSize)]
        public int Size { get; set; }
    }

    public class DevObjectOfferResult
    {
        /// <summary>
        ///   The SHA3s of objects the server wants
        /// </summary>
        [JsonPropertyName("upload")]
        public List<string> Upload { get; set; } = new();
    }

    public class DevBuildUploadRequest
    {
        [Required]
        [JsonPropertyName("build_hash")]
        [MinLength(5)]
        [MaxLength(100)]
        public string BuildHash { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("build_branch")]
        [MinLength(2)]
        [MaxLength(100)]
        public string BuildBranch { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("build_platform")]
        [MaxLength(255)]
        public string BuildPlatform { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("build_size")]
        [Range(1, AppInfo.MaxDevBuildUploadSize)]
        public int BuildSize { get; set; }

        [Required]
        [JsonPropertyName("build_zip_hash")]
        [MinLength(2)]
        [MaxLength(100)]
        public string BuildZipHash { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("required_objects")]
        [MaxLength(AppInfo.MaxDehydratedObjectsInDevBuild)]
        public List<string> RequiredDehydratedObjects { get; set; } = new();
    }

    public class DevBuildUploadResult
    {
        public DevBuildUploadResult(string uploadUrl, string verifyToken)
        {
            UploadUrl = uploadUrl;
            VerifyToken = verifyToken;
        }

        [JsonPropertyName("upload_url")]
        public string UploadUrl { get; set; }

        [JsonPropertyName("verify_token")]
        public string VerifyToken { get; set; }
    }

    public class DehydratedUploadRequest
    {
        [Required]
        [MaxLength(AppInfo.MaxDehydratedObjectsPerOffer)]
        public List<DehydratedObjectRequest> Objects { get; set; } = new();
    }

    public class DehydratedUploadResult
    {
        [JsonPropertyName("upload")]
        public List<ObjectToUpload> Upload { get; set; } = new();

        public class ObjectToUpload
        {
            public ObjectToUpload(string sha3, string uploadUrl, string verifyToken)
            {
                Sha3 = sha3;
                UploadUrl = uploadUrl;
                VerifyToken = verifyToken;
            }

            [JsonPropertyName("sha3")]
            public string Sha3 { get; set; }

            [JsonPropertyName("upload_url")]
            public string UploadUrl { get; set; }

            [JsonPropertyName("verify_token")]
            public string VerifyToken { get; set; }
        }
    }
}
