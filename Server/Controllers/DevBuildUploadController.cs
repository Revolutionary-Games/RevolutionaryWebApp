using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Forms;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/devbuild")]
    public class DevBuildUploadController : Controller
    {
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

        public DevBuildUploadController(ILogger<DevBuildUploadController> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
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
                    database.DevBuilds.AsQueryable().Where(b =>
                        b.BuildHash == request.BuildHash && b.Platform == request.BuildPlatform);
            }
            else
            {
                query =
                    database.DevBuilds.AsQueryable().Where(b =>
                        b.BuildHash == request.BuildHash && b.Platform == request.BuildPlatform &&
                        b.Anonymous == false);
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
            var failResult = GetAccessStatus(out var anonymous);
            if (failResult != null)
                return failResult;

            var result = new DevObjectOfferResult();

            foreach (var obj in request.Objects)
            {
                var existing = await database.DehydratedObjects.Include(d => d.StorageItem)
                    .FirstOrDefaultAsync(d => d.Sha3 == obj.Sha3);

                if (existing != null)
                {
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

            var existing = await database.DevBuilds.AsQueryable().FirstOrDefaultAsync(d =>
                d.BuildHash == request.BuildHash && d.Platform == request.BuildPlatform);

            if (anonymous)
            {
                if (existing != null)
                {
                    return Unauthorized("Can't upload over an existing build without an access key");
                }
            }
            else
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

            throw new NotImplementedException();

            var folder = await StorageItem.GetDevBuildBuildsFolder(database);
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

            throw new NotImplementedException();
        }

        /// <summary>
        ///   Report finished devbuild / object upload
        /// </summary>
        [HttpPost("finish")]
        public async Task<IActionResult> FinishUpload([Required] [FromBody] TokenForm request)
        {
            var failResult = GetAccessStatus(out var anonymous);
            if (failResult != null)
                return failResult;

            throw new NotImplementedException();
        }

        /// <summary>
        ///   Checks the access to the devbuild endpoint
        /// </summary>
        /// <param name="anonymous">Whether this is an anonymous (unsafe) upload or not</param>
        /// <returns>A failure result. If not null the main action should return this</returns>
        [NonAction]
        private ActionResult GetAccessStatus(out bool anonymous)
        {
            anonymous = false;

            switch (HttpContext.HasAuthenticatedAccessKeyExtended(AccessKeyType.DevBuilds))
            {
                case HttpContextAuthorizationExtensions.AuthenticationResult.NoUser:
                    anonymous = true;
                    break;
                case HttpContextAuthorizationExtensions.AuthenticationResult.NoAccess:
                    return Forbid();
                case HttpContextAuthorizationExtensions.AuthenticationResult.Success:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return null;
        }
    }

    public class DevBuildOfferRequest
    {
        [Required]
        [JsonPropertyName("build_hash")]
        public string BuildHash { get; set; }

        [Required]
        [JsonPropertyName("build_platform")]
        public string BuildPlatform { get; set; }
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
        public List<DehydratedObjectRequest> Objects { get; set; }
    }

    public class DehydratedObjectRequest
    {
        [Required]
        [MinLength(5)]
        [MaxLength(100)]
        public string Sha3 { get; set; }

        [Required]
        [Range(1, AppInfo.MaxDehydratedUploadSize)]
        public long Size { get; set; }
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
        public string BuildHash { get; set; }

        [Required]
        [JsonPropertyName("build_branch")]
        [MinLength(2)]
        [MaxLength(100)]
        public string BuildBranch { get; set; }

        [Required]
        [JsonPropertyName("build_platform")]
        public string BuildPlatform { get; set; }

        [Required]
        [JsonPropertyName("build_size")]
        [Range(1, AppInfo.MaxDehydratedObjectsInDevBuild)]
        public long BuildSize { get; set; }

        [Required]
        [JsonPropertyName("build_zip_hash")]
        [MinLength(2)]
        [MaxLength(100)]
        public string BuildZipHash { get; set; }

        [Required]
        [JsonPropertyName("required_objects")]
        [MaxLength(AppInfo.MaxDehydratedObjectsInDevBuild)]
        public List<string> RequiredDehydratedObjects { get; set; }
    }

    public class DevBuildUploadResult
    {
    }

    public class DehydratedUploadRequest
    {
        [Required]
        [MaxLength(AppInfo.MaxDehydratedObjectsPerOffer)]
        public List<DehydratedObjectRequest> Objects { get; set; }
    }

    public class DehydratedUploadResult
    {
    }
}
