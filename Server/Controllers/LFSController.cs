using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Authorization;
    using Filters;
    using Microsoft.AspNetCore.DataProtection;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc.Formatters;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared;
    using Shared.Models;
    using Shared.Utilities;
    using Utilities;

    [ApiController]
    [Route("api/v1/lfs")]
    public class LFSController : Controller
    {
        private const string LfsUploadProtectionPurposeString = "LFSController.Upload.v1";

        private static readonly TimeSpan UploadValidTime = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan S3UploadValidTime = UploadValidTime + TimeSpan.FromSeconds(15);
        private static readonly TimeSpan UploadTokenValidTime = UploadValidTime + TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DownloadExpireTime = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan DownloadUrlExpireTime = DownloadExpireTime + TimeSpan.FromSeconds(10);

        private readonly ILogger<LFSController> logger;
        private readonly ApplicationDbContext database;
        private readonly LfsDownloadUrls downloadUrls;
        private readonly LfsRemoteStorage remoteStorage;
        private readonly IConfiguration configuration;
        private readonly ITimeLimitedDataProtector dataProtector;

        private bool bucketChecked;

        public LFSController(ILogger<LFSController> logger,
            ApplicationDbContext database, LfsDownloadUrls downloadUrls, IDataProtectionProvider dataProtectionProvider,
            LfsRemoteStorage remoteStorage, IConfiguration configuration)
        {
            this.logger = logger;
            this.database = database;
            this.downloadUrls = downloadUrls;
            this.remoteStorage = remoteStorage;
            this.configuration = configuration;

            dataProtector = dataProtectionProvider.CreateProtector(LfsUploadProtectionPurposeString)
                .ToTimeLimitedDataProtector();
        }

        [HttpGet("{slug}")]
        public async Task<ActionResult<LFSProjectInfo>> GetBasicInfo([Required] string slug)
        {
            var project = await database.LfsProjects.AsQueryable().Where(p => p.Slug == slug && p.Deleted != true)
                .FirstOrDefaultAsync();

            if (project == null)
                return NotFound("Not found or project is private");

            if (!project.Public)
            {
                if (!HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Developer, null))
                {
                    return NotFound("Not found or project is private");
                }
            }

            return project.GetInfo();
        }

        [HttpPost("{slug}/objects/batch")]
        public async Task<ActionResult<LFSResponse>> BatchOperation([Required] string slug,
            [Required] LFSRequest request)
        {
            SetContentType();

            if (request == null || !request.SupportsBasicTransfer)
                return CreateErrorResult("Only basic transfer adapter is supported");

            var project = await database.LfsProjects.AsQueryable().Where(p => p.Slug == slug && p.Deleted != true)
                .FirstOrDefaultAsync();

            if (project?.Public != true)
            {
                if (!RequireWriteAccess(out var result))
                    return result;

                return NotFound("Not found");
            }

            if (!remoteStorage.Configured || !downloadUrls.Configured)
            {
                throw new HttpResponseException()
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Value = new BasicJSONErrorResult("Storage is not configured",
                        "LFS storage on the server side is not configured properly").ToString()
                };
            }

            List<LFSResponse.LFSObject> objects;

            try
            {
                objects = await HandleLFSObjectRequests(project, request.Operation, request.Objects);
            }
            catch (InvalidAccessException e)
            {
                logger.LogWarning("LFS operation was attempted without authentication / authorization");
                return e.Result;
            }

            if (objects.Count < 1)
            {
                return new ObjectResult(new BasicJSONErrorResult("No processable objects",
                        "No valid objects found in request to process")
                    .ToString())
                {
                    StatusCode = StatusCodes.Status422UnprocessableEntity,
                    ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
                };
            }

            var status = StatusCodes.Status200OK;

            // If all objects are in an error state, also use that for the primary return value
            if (objects.All(o => o.Error != null))
            {
                status = objects.Where(o => o.Error != null).Select(o => o.Error.Code).First();
            }

            return new ObjectResult(
                new LFSResponse
                {
                    Objects = objects
                })
            {
                StatusCode = status,
                ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
            };
        }

        [HttpPost("{slug}/verify")]
        public async Task<IActionResult> VerifyUpload([Required] string slug, [Required] string token)
        {
            SetContentType();

            // Verify token first as there is no other protection on this endpoint
            UploadVerifyToken verifiedToken;

            try
            {
                verifiedToken = JsonSerializer.Deserialize<UploadVerifyToken>(dataProtector.Unprotect(token));

                if (verifiedToken == null)
                    throw new Exception("deserialized token is null");
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to verify LFS upload token: {@E}", e);
                return new ObjectResult(new BasicJSONErrorResult("Invalid token",
                        "Invalid upload verify token provided")
                    .ToString())
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
                };
            }

            var project = await database.LfsProjects.AsQueryable().Where(p => p.Slug == slug && p.Deleted != true)
                .FirstOrDefaultAsync();

            if (project == null)
                return NotFound();

            var existingObject = await database.LfsObjects.AsQueryable()
                .Where(o => o.LfsProjectId == project.Id && o.LfsOid == verifiedToken.Oid).FirstOrDefaultAsync();

            if (existingObject != null)
            {
                logger.LogWarning("Duplicate LFS oid attempted to be verified: {Oid}", verifiedToken.Oid);
                return new ObjectResult(new BasicJSONErrorResult("Duplicate object",
                        "Object with the given OID has already been verified")
                    .ToString())
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
                };
            }

            var finalStoragePath = LfsDownloadUrls.OidStoragePath(project, verifiedToken.Oid);
            var uploadStoragePath = "uploads/" + finalStoragePath;

            try
            {
                var actualSize = await remoteStorage.GetObjectSize(uploadStoragePath);

                if (actualSize != verifiedToken.Size)
                {
                    logger.LogWarning("Detected partial upload to remote storage");
                    return new ObjectResult(new BasicJSONErrorResult("Verification failed",
                            "The object size in remote storage is different than it should be")
                        .ToString())
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
                    };
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to check object size in storage: {@E}", e);
                return new ObjectResult(new BasicJSONErrorResult("Verification failed",
                        "Failed to retrieve the object size")
                    .ToString())
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
                };
            }

            try
            {
                // Move the uploaded file to a path the user can't anymore access to overwrite it
                await remoteStorage.MoveObject(uploadStoragePath, finalStoragePath);

                // Check the stored file hash
                var actualHash = await remoteStorage.ComputeSha256OfObject(finalStoragePath);

                if (actualHash != verifiedToken.Oid)
                {
                    logger.LogWarning("Uploaded file OID doesn't match: {Oid}, actual: {ActualHash}", verifiedToken.Oid,
                        actualHash);

                    logger.LogInformation("Attempting to delete the copied invalid file");

                    await remoteStorage.DeleteObject(finalStoragePath);

                    return new ObjectResult(new BasicJSONErrorResult("Verification failed",
                            "The file you uploaded doesn't match the oid you claimed it to be")
                        .ToString())
                    {
                        StatusCode = StatusCodes.Status400BadRequest,
                        ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
                    };
                }
            }
            catch (Exception e)
            {
                logger.LogError("Upload verify storage operation failed: {@E}", e);
                return new ObjectResult(new BasicJSONErrorResult("Internal storage operation failed",
                        "Some remote storage operation failed while processing the file")
                    .ToString())
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                    ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
                };
            }

            // Everything has been verified now so we can save the object
            // TODO: store the user Id who uploaded the object / if this was anonymous (for PRs)
            await database.LfsObjects.AddAsync(new LfsObject()
            {
                LfsOid = verifiedToken.Oid,
                Size = verifiedToken.Size,
                LfsProjectId = project.Id,
                StoragePath = finalStoragePath
            });
            await database.SaveChangesAsync();

            // TODO: queue one more hash check to happen in 30 minutes to ensure the file is right to avoid any possible
            // timing attack against managing to replace the file with incorrect content

            logger.LogInformation("New LFS object uploaded: {Oid} for project: {Name}", verifiedToken.Oid,
                project.Name);

            return Ok();
        }

        [HttpPost("{slug}/locks/verify")]
        public IActionResult Locks([Required] string slug)
        {
            return new ObjectResult(new BasicJSONErrorResult("Unimplemented",
                    "LFS locks API is unimplemented")
                .ToString())
            {
                StatusCode = StatusCodes.Status501NotImplemented,
                ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
            };
        }

        // TODO: would there be a way to call this automatically?
        [NonAction]
        private void SetContentType()
        {
            HttpContext.Response.ContentType = AppInfo.GitLfsContentType;
        }

        [NonAction]
        private bool RequireWriteAccess(out ActionResult resultIfFailed)
        {
            switch (HttpContext.HasAuthenticatedUserWithAccessExtended(UserAccessLevel.Developer,
                AuthenticationScopeRestriction.LFSOnly))
            {
                case HttpContextAuthorizationExtensions.AuthenticationResult.NoUser:
                    resultIfFailed = RequestAuthResult();
                    return false;
                case HttpContextAuthorizationExtensions.AuthenticationResult.NoAccess:
                    resultIfFailed = InvalidAccountResult();
                    return false;
                case HttpContextAuthorizationExtensions.AuthenticationResult.Success:
                    resultIfFailed = null;
                    return true;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        [NonAction]
        private void SetAuthenticateHeader()
        {
            HttpContext.Response.Headers["LFS-Authenticate"] = "Basic realm=\"ThriveDevCenter Git LFS\"";
        }

        [NonAction]
        private ActionResult RequestAuthResult()
        {
            SetAuthenticateHeader();
            return Unauthorized(new BasicJSONErrorResult("Authentication required",
                    "For help see: https://wiki.revolutionarygamesstudio.com/wiki/Git_LFS")
                .ToString());
        }

        [NonAction]
        private ActionResult InvalidAccountResult()
        {
            SetAuthenticateHeader();

            var result = new ObjectResult(new BasicJSONErrorResult("Invalid credentials",
                    "Invalid credentials or you don't have write access or your account is suspended. " +
                    "For help see: https://wiki.revolutionarygamesstudio.com/wiki/Git_LFS")
                .ToString())
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
            };

            return result;
        }

        [NonAction]
        private ActionResult CreateErrorResult(string message)
        {
            var result = new ObjectResult(new BasicJSONErrorResult("Bad request",
                    message)
                .ToString())
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = new MediaTypeCollection() { AppInfo.GitLfsContentType }
            };

            return result;
        }

        [NonAction]
        private async ValueTask<LFSResponse.LFSObject> HandleDownload(LfsProject project, LFSRequest.LFSObject obj)
        {
            var existingObject = await database.LfsObjects.AsQueryable()
                .Where(o => o.LfsOid == obj.Oid && o.LfsProjectId == project.Id).Include(o => o.LfsProject)
                .FirstOrDefaultAsync();

            if (existingObject == null)
            {
                return new LFSResponse.LFSObject(obj.Oid, obj.Size,
                    new LFSResponse.LFSObject.ErrorInfo(StatusCodes.Status404NotFound, "OID not found"));
            }

            var createdUrl = downloadUrls.CreateDownloadFor(existingObject, DownloadUrlExpireTime);

            return new LFSResponse.LFSObject(obj.Oid, obj.Size)
            {
                Actions = new Dictionary<string, LFSResponse.LFSObject.Action>()
                {
                    {
                        "download", new LFSResponse.LFSObject.DownloadAction()
                        {
                            Href = createdUrl,
                            ExpiresIn = (int)DownloadExpireTime.TotalSeconds
                        }
                    }
                }
            };
        }

        [NonAction]
        private string GenerateUploadVerifyToken(LFSRequest.LFSObject obj)
        {
            var token = new UploadVerifyToken()
            {
                Oid = obj.Oid,
                Size = obj.Size
            };

            var value = JsonSerializer.Serialize(token);

            return dataProtector.Protect(value, UploadTokenValidTime);
        }

        [NonAction]
        private async ValueTask<LFSResponse.LFSObject> HandleUpload(LfsProject project, LFSRequest.LFSObject obj)
        {
            var existingObject = await database.LfsObjects.AsQueryable()
                .Where(o => o.LfsOid == obj.Oid && o.LfsProjectId == project.Id).Include(o => o.LfsProject)
                .FirstOrDefaultAsync();

            if (existingObject != null)
            {
                // We already have this object
                return new LFSResponse.LFSObject(obj.Oid, obj.Size)
                {
                    Actions = null,
                    Authenticated = null
                };
            }

            if (obj.Size > AppInfo.MaxLfsUploadSize)
            {
                return new LFSResponse.LFSObject(obj.Oid, obj.Size,
                    new LFSResponse.LFSObject.ErrorInfo(StatusCodes.Status422UnprocessableEntity, "File is too large"));
            }

            logger.LogTrace("Requesting auth because new object is to be uploaded {Oid} for project {Name}", obj.Oid,
                project.Name);

            // New object. User must have write access
            if (!RequireWriteAccess(out var result))
                throw new InvalidAccessException(result);

            // We don't yet create the LfsObject here to guard against upload failures
            // instead the verify callback does that

            // The uploads prefix is used here to ensure the user can't overwrite the file after uploading and
            // verification
            var storagePath = "uploads/" + LfsDownloadUrls.OidStoragePath(project, obj.Oid);

            if (!bucketChecked)
            {
                try
                {
                    if (!await remoteStorage.BucketExists())
                    {
                        throw new Exception("bucket doesn't exist");
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning("Bucket check failed: {@E}", e);
                    var error = "remote storage is inaccessible";
                    throw new HttpResponseException()
                    {
                        Status = StatusCodes.Status500InternalServerError,
                        Value = new BasicJSONErrorResult(error, error).ToString()
                    };
                }

                bucketChecked = true;
            }

            var verifyUrl = QueryHelpers.AddQueryString(
                new Uri(configuration.GetBaseUrl(), $"api/v1/lfs/{project.Slug}/verify").ToString(),
                "token", GenerateUploadVerifyToken(obj));

            return new LFSResponse.LFSObject(obj.Oid, obj.Size)
            {
                Actions = new Dictionary<string, LFSResponse.LFSObject.Action>()
                {
                    {
                        "upload", new LFSResponse.LFSObject.UploadAction()
                        {
                            Href = remoteStorage.CreatePresignedUploadURL(storagePath, S3UploadValidTime),
                            ExpiresIn = (int)UploadValidTime.TotalSeconds
                        }
                    },
                    {
                        "verify", new LFSResponse.LFSObject.UploadAction()
                        {
                            Href = verifyUrl,
                            ExpiresIn = (int)UploadValidTime.TotalSeconds
                        }
                    }
                }
            };
        }

        private async Task<List<LFSResponse.LFSObject>> HandleLFSObjectRequests(LfsProject project,
            LFSRequest.OperationType operation,
            IEnumerable<LFSRequest.LFSObject> objects)
        {
            switch (operation)
            {
                case LFSRequest.OperationType.Download:
                    return await objects.ToAsyncEnumerable().SelectAwait(o => HandleDownload(project, o)).ToListAsync();
                case LFSRequest.OperationType.Upload:
                    return await objects.ToAsyncEnumerable().SelectAwait(o => HandleUpload(project, o)).ToListAsync();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private class UploadVerifyToken
        {
            public string Oid { get; set; }
            public long Size { get; set; }
        }

        private class InvalidAccessException : Exception
        {
            public InvalidAccessException(ActionResult result)
            {
                Result = result;
            }

            public ActionResult Result { get; }
        }
    }

    public class LFSRequest
    {
        [Required]
        public OperationType Operation { get; set; }

        public List<string> Transfers { get; set; }

        public LFSRef Ref { get; set; }

        [Required]
        [MaxLength(200)]
        public List<LFSObject> Objects { get; set; }

        [JsonIgnore]
        public bool SupportsBasicTransfer
        {
            get
            {
                // Assume basic is supported if nothing is provided
                if (Transfers == null)
                    return true;

                // otherwise basic needs to be defined
                return Transfers.Any(t => t == "basic");
            }
        }

        [JsonConverter(typeof(ActualEnumStringConverter))]
        public enum OperationType
        {
            [EnumMember(Value = "download")]
            Download,

            [EnumMember(Value = "download")]
            Upload
        }

        public class LFSObject
        {
            [Required]
            public string Oid { get; set; }

            [Required]
            [Range(0, long.MaxValue)]
            public long Size { get; set; }
        }

        public class LFSRef
        {
            [Required]
            public string Name { get; set; }
        }
    }

    public class LFSResponse
    {
        [JsonPropertyName("transfer")]
        public string Transfer { get; set; } = "basic";

        [JsonPropertyName("objects")]
        public List<LFSObject> Objects { get; set; } = new List<LFSObject>();

        public class LFSObject
        {
            [Required]
            [JsonPropertyName("oid")]
            [StringLength(128, MinimumLength = 5)]
            public string Oid { get; set; }

            [Required]
            [Range(0, long.MaxValue)]
            [JsonPropertyName("size")]
            public long Size { get; set; }

            [Required]
            [JsonPropertyName("authenticated")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public bool? Authenticated { get; set; } = true;

            [JsonPropertyName("actions")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public Dictionary<string, Action> Actions { get; set; }

            [JsonPropertyName("error")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public ErrorInfo Error { get; set; }

            public LFSObject(string oid, long size)
            {
                Oid = oid;
                Size = size;
            }

            public LFSObject(string oid, long size, ErrorInfo error)
            {
                Error = error;
                Authenticated = null;
            }

            public abstract class Action
            {
                [Required]
                [JsonPropertyName("href")]
                public string Href { get; set; }

                [JsonPropertyName("expires_in")]
                public int ExpiresIn { get; set; }

                [JsonPropertyName("header")]
                public Dictionary<string, string> Header { get; set; }
            }

            public class DownloadAction : Action
            {
            }

            public class UploadAction : Action
            {
            }

            public class VerifyAction : Action
            {
            }

            public class ErrorInfo
            {
                [Required]
                [JsonPropertyName("code")]
                public int Code { get; set; }

                [Required]
                [JsonPropertyName("message")]
                public string Message { get; set; }

                public ErrorInfo(int code, string message)
                {
                    Code = code;
                    Message = message;
                }
            }
        }
    }
}
