using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Threading.Tasks;
    using Models;

    [Controller]
    [Route("[controller]")]
    public class LogoutController : Controller
    {
        [HttpDelete]
        public IActionResult Logout()
        {
            return BadRequest("not implemented");
        }

        internal static Task PerformSessionDestroy(Session session, ApplicationDbContext database)
        {
            // TODO: could setup a hub group for each session to receive session specific messages
            database.Sessions.Remove(session);
            return database.SaveChangesAsync();
        }
    }
}
