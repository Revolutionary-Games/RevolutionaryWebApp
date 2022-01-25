using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using Services;
    using Shared;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/download_lfs")]
    public class LFSFileDownloadController : Controller
    {
        private readonly ApplicationDbContext database;
        private readonly LfsDownloadUrls downloadUrls;

        public LFSFileDownloadController(ApplicationDbContext database, LfsDownloadUrls downloadUrls)
        {
            this.database = database;
            this.downloadUrls = downloadUrls;
        }

        /// <summary>
        ///   Single LFS file download API meant for browsers to use
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Download([Required] long project, [Required] [MaxLength(1024)] string path,
            [Required] [MaxLength(1024)] string name)
        {
            var projectObject = await database.LfsProjects.FindAsync(project);

            if (projectObject == null || projectObject.Deleted || (!projectObject.Public &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Developer, null)))
            {
                return NotFound("Invalid project specified, or you don't have access. Logging in may help");
            }

            var file = await database.ProjectGitFiles.FirstOrDefaultAsync(p =>
                p.LfsProjectId == projectObject.Id && p.Name == name && p.Path == path);

            if (file == null)
                return NotFound("File not found in project");

            if (string.IsNullOrEmpty(file.LfsOid))
                return BadRequest("File is non-lfs file");

            var lfsObject =
                await database.LfsObjects.FirstOrDefaultAsync(o =>
                    o.LfsProjectId == projectObject.Id && o.LfsOid == file.LfsOid);

            if (lfsObject == null)
                return BadRequest("Required LFS object doesn't exist");

            return Redirect(downloadUrls.CreateDownloadFor(lfsObject, AppInfo.RemoteStorageDownloadExpireTime));
        }
    }
}
