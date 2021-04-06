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
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class AccessKeyController : Controller
    {
        private readonly ILogger<AccessKeyController> logger;
        private readonly ApplicationDbContext database;

        public AccessKeyController(ILogger<AccessKeyController> logger,
            ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet]
        public async Task<PagedResult<AccessKeyDTO>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 50)] int pageSize)
        {
            IQueryable<AccessKey> query;

            try
            {
                query = database.AccessKeys.AsQueryable().OrderBy(sortColumn, sortDirection);
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
