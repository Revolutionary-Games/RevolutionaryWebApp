namespace ThriveDevCenter.Server.Utilities
{
    using Filters;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;

    public static class ControllerExtensions
    {
        public static ActionResult WorkingForbid(this Controller controller, string message)
        {
            throw new HttpResponseException()
            {
                Status = StatusCodes.Status403Forbidden,
                Value = message
            };
        }
    }
}
