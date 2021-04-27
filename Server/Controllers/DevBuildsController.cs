using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class DevBuildsController : Controller
    {
        private readonly ILogger<DevBuildsController> logger;
        private readonly NotificationsEnabledDb database;

        public DevBuildsController(ILogger<DevBuildsController> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpGet]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<DevBuildsStatisticsDTO>> Get()
        {
            var buildsSize = await database.DevBuilds.AsQueryable()
                .Include(b => b.StorageItem).SumAsync(b =>
                    b.StorageItem.Size.HasValue ? Convert.ToInt64(b.StorageItem.Size.Value) : 0L);

            var dehydratedSize = await database.DehydratedObjects.AsQueryable().Include(d => d.StorageItem)
                .SumAsync(b => b.StorageItem.Size.HasValue ? Convert.ToInt64(b.StorageItem.Size.Value) : 0L);

            var totalBuilds = await database.DevBuilds.AsQueryable().CountAsync();

            DateTime? botdCreated = null;

            foreach (var build in await database.DevBuilds.AsQueryable().Where(b => b.BuildOfTheDay).ToListAsync())
            {
                if (botdCreated == null || build.CreatedAt > botdCreated)
                {
                    botdCreated = build.CreatedAt;
                }
            }

            DateTime? latestBuild = null;

            if (totalBuilds > 0)
            {
                latestBuild = await database.DevBuilds.AsQueryable().MaxAsync(b => b.CreatedAt);
            }

            var result = new DevBuildsStatisticsDTO
            {
                TotalBuilds = totalBuilds,
                TotalDownloads = await database.DevBuilds.AsQueryable().SumAsync(b => b.Downloads),
                DehydratedFiles = await database.DehydratedObjects.AsQueryable().CountAsync(),
                ImportantBuilds = await database.DevBuilds.AsQueryable().CountAsync(b => b.Important),
                BOTDUpdated = botdCreated,
                LatestBuild = latestBuild,
                DevBuildsSize = buildsSize,
                TotalSpaceUsed = buildsSize + dehydratedSize
            };

            return result;
        }
    }
}
