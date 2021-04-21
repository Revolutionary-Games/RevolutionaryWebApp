using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/devbuild")]
    public class DevBuildUploadController : Controller
    {
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
            bool anonymous = false;

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
}
