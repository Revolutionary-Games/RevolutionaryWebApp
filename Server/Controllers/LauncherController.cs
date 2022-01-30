using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Authorization;
    using Filters;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared;
    using Shared.Models;
    using Shared.Utilities;
    using Utilities;

    [ApiController]
    [Route("api/v1/launcher")]
    public class LauncherController : Controller
    {
        private readonly ILogger<LauncherController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IGeneralRemoteDownloadUrls remoteDownloads;

        public LauncherController(ILogger<LauncherController> logger, NotificationsEnabledDb database,
            IGeneralRemoteDownloadUrls remoteDownloads)
        {
            this.logger = logger;
            this.database = database;
            this.remoteDownloads = remoteDownloads;
        }

        /// <summary>
        ///   Checks if launcher link code is valid, doesn't consume the code
        /// </summary>
        [HttpPost("check_link")]
        public async Task<ActionResult<LauncherConnectionStatus>> CheckNewLinkCode(
            [Required] LauncherLinkCodeCheckForm request)
        {
            Response.ContentType = "application/json";
            var user = await GetUserForNewLink(request.Code);

            return new LauncherConnectionStatus()
            {
                Valid = true,
                Username = user.UserName ?? user.Email,
                Email = user.Email,
                Developer = user.HasAccessLevel(UserAccessLevel.Developer)
            };
        }

        [HttpPost("link")]
        public async Task<IActionResult> ConnectLauncher([Required] LauncherLinkCodeCheckForm request)
        {
            Response.ContentType = "application/json";
            var user = await GetUserForNewLink(request.Code);

            // Update user to consume the code
            user.LauncherCodeExpires = DateTime.UtcNow - TimeSpan.FromSeconds(1);
            user.LauncherLinkCode = null;
            user.TotalLauncherLinks += 1;

            // Create a new code, which the user doesn't directly see to avoid it leaking as easily
            var code = NonceGenerator.GenerateNonce(42);

            var remoteAddress = HttpContext.Connection.RemoteIpAddress;

            await database.LauncherLinks.AddAsync(new LauncherLink()
            {
                User = user,
                LinkCode = code,
                LastIp = remoteAddress?.ToString(),
                LastConnection = DateTime.UtcNow
            });

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"New launcher link created from: {remoteAddress}",
                TargetUserId = user.Id
            });

            await database.SaveChangesAsync();

            logger.LogInformation("New launcher linked to user {Id} from {RemoteAddress}", user.Id, remoteAddress);

            return Created(string.Empty, new LauncherLinkResult()
            {
                Connected = true,
                Code = code,
            });
        }

        /// <summary>
        ///   Checks launcher connection status
        /// </summary>
        [HttpGet("status")]
        [AuthorizeRoleFilter(RequiredRestriction = nameof(AuthenticationScopeRestriction.LauncherOnly))]
        public ActionResult<LauncherConnectionStatus> CheckStatus()
        {
            Response.ContentType = "application/json";
            var user = HttpContext.AuthenticatedUser();

            return new LauncherConnectionStatus()
            {
                Valid = true,
                Username = user.UserName ?? user.Email,
                Email = user.Email,
                Developer = user.HasAccessLevel(UserAccessLevel.Developer)
            };
        }

        [HttpDelete("status")]
        [AuthorizeRoleFilter(RequiredRestriction = nameof(AuthenticationScopeRestriction.LauncherOnly))]
        public async Task<ActionResult<LauncherUnlinkResult>> Disconnect()
        {
            Response.ContentType = "application/json";
            var user = HttpContext.AuthenticatedUser();

            var remoteAddress = HttpContext.Connection.RemoteIpAddress;

            var link = HttpContext.UsedLauncherLink();

            if (link == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = new BasicJSONErrorResult("Link not found",
                        "Used link object was not found").ToString()
                };
            }

            database.LauncherLinks.Remove(link);

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"Launcher disconnected through the launcher API from: {remoteAddress}",
                TargetUserId = user.Id
            });

            await database.SaveChangesAsync();

            logger.LogInformation("Launcher disconnected from user {Id} from {RemoteAddress} through the launcher API",
                user.Id, remoteAddress);

            return new LauncherUnlinkResult()
            {
                Success = true
            };
        }

        [HttpGet("builds/download/{buildId:long}")]
        [AuthorizeRoleFilter(RequiredRestriction = nameof(AuthenticationScopeRestriction.LauncherOnly))]
        public async Task<ActionResult<DevBuildDownload>> DownloadBuild([Required] long buildId)
        {
            Response.ContentType = "application/json";

            var build = await database.DevBuilds.Include(b => b.StorageItem)
                .FirstOrDefaultAsync(b => b.Id == buildId);

            if (build == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status404NotFound,
                    Value = new BasicJSONErrorResult("Build not found",
                        "Build with specified ID not found").ToString()
                };
            }

            if (build.StorageItem == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status404NotFound,
                    Value = new BasicJSONErrorResult("Invalid build",
                        "The specified build doesn't have a valid download file").ToString()
                };
            }

            if (!build.StorageItem.IsReadableBy(HttpContext.AuthenticatedUser()))
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status403Forbidden,
                    Value = new BasicJSONErrorResult("No access",
                        "You don't have permission to access this build's download file").ToString()
                };
            }

            var version = await build.StorageItem.GetHighestUploadedVersion(database);

            if (version == null || version.StorageFile == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status404NotFound,
                    Value = new BasicJSONErrorResult("Invalid build",
                        "The specified build's storage doesn't have a valid uploaded file").ToString()
                };
            }

            build.Downloads += 1;
            logger.LogInformation("DevBuild {Id} downloaded from {RemoteAddress} with the launcher",
                build.Id, HttpContext.Connection.RemoteIpAddress);

            await database.SaveChangesAsync();

            return new DevBuildDownload()
            {
                DownloadUrl =
                    remoteDownloads.CreateDownloadFor(version.StorageFile, AppInfo.RemoteStorageDownloadExpireTime),
                DownloadHash = build.BuildZipHash
            };
        }

        /// <summary>
        ///   Gets currently available devbuild information (latest builds)
        /// </summary>
        [HttpPost("builds")]
        [AuthorizeRoleFilter(RequiredRestriction = nameof(AuthenticationScopeRestriction.LauncherOnly))]
        public async Task<ActionResult<DevBuildSearchResults>> GetBuilds(
            [Required] [FromBody] DevBuildSearchForm request)
        {
            Response.ContentType = "application/json";

            var query = database.DevBuilds.AsQueryable();

            if (!string.IsNullOrEmpty(request.Platform))
            {
                query = query.Where(b => b.Platform == request.Platform);
            }

            var items = await query.OrderByDescending(b => b.CreatedAt).Skip(request.Offset).Take(request.PageSize + 1)
                .ToListAsync();

            var result = new DevBuildSearchResults();

            // TODO: could convert this part to an extension method
            if (items.Count > request.PageSize)
            {
                items.Remove(items.Last());
                result.NextOffset = request.Offset + items.Count;
            }

            result.Result = items.Select(b => b.GetLauncherDTO()).ToList();
            return result;
        }

        /// <summary>
        ///   Searches for a devbuild based on the commit hash
        /// </summary>
        [HttpPost("search")]
        [AuthorizeRoleFilter(RequiredRestriction = nameof(AuthenticationScopeRestriction.LauncherOnly))]
        public async Task<ActionResult<DevBuildSearchResults>> SearchByHash(
            [Required] [FromBody] DevBuildHashSearchForm request)
        {
            Response.ContentType = "application/json";

            var query = database.DevBuilds.Where(b => b.BuildHash == request.BuildHash);

            if (!string.IsNullOrEmpty(request.Platform))
            {
                query = query.Where(b => b.Platform == request.Platform);
            }

            var items = await query.OrderByDescending(b => b.CreatedAt).Skip(request.Offset).Take(request.PageSize + 1)
                .ToListAsync();

            var result = new DevBuildSearchResults();

            // TODO: could convert this part to an extension method
            if (items.Count > request.PageSize)
            {
                items.Remove(items.Last());
                result.NextOffset = request.Offset + items.Count;
            }

            result.Result = items.Select(b => b.GetLauncherDTO()).ToList();
            return result;
        }

        /// <summary>
        ///   Searches for a devbuild based on it being the build of the day or latest
        /// </summary>
        [HttpPost("find")]
        [AuthorizeRoleFilter(RequiredRestriction = nameof(AuthenticationScopeRestriction.LauncherOnly))]
        public async Task<ActionResult<DevBuildLauncherDTO>> SearchByType(
            [Required] [FromBody] DevBuildFindByTypeForm request)
        {
            Response.ContentType = "application/json";

            DevBuild build;

            switch (request.Type)
            {
                case DevBuildFindByTypeForm.BuildType.BuildOfTheDay:
                    build = await database.DevBuilds.FirstOrDefaultAsync(b =>
                        b.Platform == request.Platform && b.BuildOfTheDay);
                    break;
                case DevBuildFindByTypeForm.BuildType.Latest:
                    build = await database.DevBuilds
                        .Where(b => b.Platform == request.Platform && (b.Verified || !b.Anonymous))
                        .OrderByDescending(b => b.Id)
                        .FirstOrDefaultAsync();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (build == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status404NotFound,
                    Value = new BasicJSONErrorResult("Build not found",
                        $"Could not find build with type {request.Type}").ToString()
                };
            }

            return build.GetLauncherDTO();
        }

        /// <summary>
        ///   Downloads specified list of dehydrated objects
        /// </summary>
        [HttpPost("dehydrated/download")]
        [AuthorizeRoleFilter(RequiredRestriction = nameof(AuthenticationScopeRestriction.LauncherOnly))]
        public async Task<ActionResult<DehydratedObjectDownloads>> DownloadDehydrated(
            [Required] [FromBody] DevBuildDehydratedObjectDownloadRequest request)
        {
            Response.ContentType = "application/json";

            var user = HttpContext.AuthenticatedUser();

            var objects = await request.Objects.ToAsyncEnumerable()
                .Select(i => i.Sha3)
                .SelectAwait(i => GetDehydratedFromHash(i, user)).ToListAsync();

            logger.LogInformation("{Count} dehydrated objects downloaded from {RemoteAddress} with the launcher",
                objects.Count, HttpContext.Connection.RemoteIpAddress);

            return new DehydratedObjectDownloads()
            {
                Downloads = objects
            };
        }

        [NonAction]
        private async Task<User> GetUserForNewLink(string code)
        {
            var now = DateTime.UtcNow;

            // To make accidental whitespace after or before the code less serious
            code = code.Trim();

            var user = await database.Users.WhereHashed(nameof(Models.User.LauncherLinkCode), code)
                .Where(u => u.LauncherCodeExpires != null && u.LauncherCodeExpires >= now).Include(u => u.LauncherLinks)
                .ToAsyncEnumerable().FirstOrDefaultAsync(u => u.LauncherLinkCode == code);

            if (user == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status403Forbidden,
                    Value = new BasicJSONErrorResult("Invalid code",
                        "Invalid authorization code or it has expired").ToString()
                };
            }

            if (user.LauncherLinks.Count >= AppInfo.DefaultMaxLauncherLinks)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status400BadRequest,
                    Value = new BasicJSONErrorResult("Too many links",
                        "You have already linked the maximum number of launchers to your account").ToString()
                };
            }

            return user;
        }

        [NonAction]
        private async ValueTask<DehydratedObjectDownloads.DehydratedObjectDownload> GetDehydratedFromHash(string sha3,
            User user)
        {
            var dehydrated = await database.DehydratedObjects.Include(d => d.StorageItem)
                .FirstOrDefaultAsync(d => d.Sha3 == sha3);

            if (dehydrated == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status404NotFound,
                    Value = new BasicJSONErrorResult("Object not found",
                        $"The specified object ({sha3}) was not found").ToString()
                };
            }

            if (dehydrated.StorageItem == null || !dehydrated.StorageItem.IsReadableBy(user))
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status403Forbidden,
                    Value = new BasicJSONErrorResult("No access",
                        $"You don't have permission to access this object's ({sha3}) download file").ToString()
                };
            }

            var version = await dehydrated.StorageItem.GetHighestUploadedVersion(database);

            if (version == null || version.StorageFile == null)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status404NotFound,
                    Value = new BasicJSONErrorResult("Not found",
                        $"The specified object's ({sha3}) storage doesn't have a valid uploaded file").ToString()
                };
            }

            return new DehydratedObjectDownloads.DehydratedObjectDownload()
            {
                Sha3 = sha3,
                DownloadUrl =
                    remoteDownloads.CreateDownloadFor(version.StorageFile, AppInfo.RemoteStorageDownloadExpireTime)
            };
        }
    }

    public class LauncherLinkCodeCheckForm
    {
        [Required]
        [MaxLength(200)]
        public string Code { get; set; }
    }

    public class LauncherLinkResult
    {
        [JsonPropertyName("connected")]
        public bool Connected { get; set; }

        [JsonPropertyName("code")]
        public string Code { get; set; }
    }

    public class LauncherConnectionStatus
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("developer")]
        public bool Developer { get; set; }
    }

    public class LauncherUnlinkResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    public class DevBuildDownload
    {
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; }

        [JsonPropertyName("dl_hash")]
        public string DownloadHash { get; set; }
    }

    public class DevBuildSearchForm
    {
        [JsonPropertyName("platform")]
        [MaxLength(200)]
        public string Platform { get; set; }

        [JsonPropertyName("offset")]
        [Range(0, int.MaxValue)]
        public int Offset { get; set; } = 0;

        [JsonPropertyName("page_size")]
        [Range(1, AppInfo.MaxPageSizeForBuildSearch)]
        public int PageSize { get; set; } = AppInfo.MaxPageSizeForBuildSearch;
    }

    public class DevBuildHashSearchForm : DevBuildSearchForm
    {
        [JsonPropertyName("devbuild_hash")]
        [MaxLength(200)]
        public string BuildHash { get; set; }
    }

    public class DevBuildFindByTypeForm
    {
        [JsonPropertyName("type")]
        [Required]
        public BuildType Type { get; set; }

        [JsonPropertyName("platform")]
        [MaxLength(200)]
        [Required]
        public string Platform { get; set; }

        [JsonConverter(typeof(ActualEnumStringConverter))]
        public enum BuildType
        {
            [EnumMember(Value = "botd")]
            BuildOfTheDay,

            [EnumMember(Value = "latest")]
            Latest
        }
    }

    public class DevBuildSearchResults
    {
        [JsonPropertyName("result")]
        public List<DevBuildLauncherDTO> Result { get; set; }

        [JsonPropertyName("next_offset")]
        public int? NextOffset { get; set; }
    }

    public class DevBuildDehydratedObjectDownloadRequest
    {
        [JsonPropertyName("objects")]
        [MaxLength(AppInfo.MaxDehydratedDownloadBatch)]
        public List<DehydratedObjectIdentification> Objects { get; set; }
    }

    public class DehydratedObjectDownloads
    {
        [JsonPropertyName("downloads")]
        public List<DehydratedObjectDownload> Downloads { get; set; }

        public class DehydratedObjectDownload : DehydratedObjectIdentification
        {
            [JsonPropertyName("download_url")]
            public string DownloadUrl { get; set; }
        }
    }
}
