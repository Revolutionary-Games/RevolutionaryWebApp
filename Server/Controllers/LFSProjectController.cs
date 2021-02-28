using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using BlazorPagination;
    using Shared;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class LFSProjectController : Controller
    {
        private readonly ILogger<LFSProjectController> _logger;

        public LFSProjectController(ILogger<LFSProjectController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public PagedResult<LFSProjectInfo> Get(string sortColumn, SortDirection sortDirection, int page, int pageSize)
        {
            var rng = new Random();

            var result = Enumerable.Range(1, 22).Select(index => new LFSProjectInfo()
            {
                Name = "Project_" + index,
                Public = true,
                Size = index * 50,
                LastUpdated = DateTime.Now + TimeSpan.FromSeconds(rng.Next(-20, 55)),
                CreatedAt = DateTime.Now - TimeSpan.FromSeconds(rng.Next(1000, 10000)),
            }).AsQueryable().OrderBy(sortColumn, sortDirection);

            return result.ToPagedResult(page, pageSize);
        }
    }
}
