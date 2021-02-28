using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using Shared;

    [ApiController]
    [Route("[controller]")]
    public class LFSProjectController : Controller
    {
        private readonly ILogger<LFSProjectController> _logger;

        public LFSProjectController(ILogger<LFSProjectController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IEnumerable<LFSProjectInfo> Get()
        {
            var rng = new Random();
            return Enumerable.Range(1, 5).Select(index => new LFSProjectInfo()
                {
                    Name = "Project_" + index,
                    Public = true,
                    Size = index * 50,
                    LastUpdated = DateTime.Now + TimeSpan.FromSeconds(rng.Next(-20, 55)),
                    CreatedAt = DateTime.Now - TimeSpan.FromSeconds(rng.Next(1000, 10000)),
                })
                .ToArray();
        }
    }
}
