namespace RevolutionaryWebApp.Server.Common.Models;

using System.Collections.Generic;
using Shared.Models;

public class AvailableJobsList
{
    public List<CIJobDTO> Jobs { get; set; } = new();
    public int FilteredCount { get; set; }
}
