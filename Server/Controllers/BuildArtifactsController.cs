using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using Authorization;
    using Shared.Forms;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class BuildArtifactsController : Controller
    {
        [HttpGet]
        public IActionResult GetForJob([Required] long projectId, [Required] long buildId, [Required] long jobId)
        {
            // TODO: implement
            return Ok();
        }

        [HttpGet("{id:long}")]
        public IActionResult Download([Required] long id)
        {
            // TODO: implement
            return Ok();
        }

        [HttpDelete("{id:long}")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        public IActionResult DeleteSingle([Required] long id)
        {
            // TODO: implement
            return Ok();
        }

        [HttpPost]
        public ActionResult<StorageUploadVerifyToken> StartUpload([Required] long projectId, [Required] long buildId,
            [Required] long jobId, ArtifactUploadRequestForm request)
        {
            // TODO: implement
            return Ok();
        }

        [HttpPost("finished")]
        public IActionResult FinishUpload([Required] [FromBody] StorageUploadVerifyToken token)
        {
            // TODO: implement
            return Ok();
        }
    }
}
