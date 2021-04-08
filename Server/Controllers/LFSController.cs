using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    [ApiController]
    [Route("api/v1/lfs")]
    public class LFSController : Controller
    {
        [HttpGet]
        public IActionResult Get()
        {
            return NotFound();
        }
    }
}
