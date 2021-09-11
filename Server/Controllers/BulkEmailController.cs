using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Hangfire;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class BulkEmailController : Controller
    {
        private readonly ILogger<BulkEmailController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public BulkEmailController(ILogger<BulkEmailController> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        [HttpGet]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<PagedResult<SentBulkEmailDTO>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<SentBulkEmail> query;

            try
            {
                query = database.SentBulkEmails.AsQueryable().OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);
            return objects.ConvertResult(i => i.GetDTO());
        }
    }
}
