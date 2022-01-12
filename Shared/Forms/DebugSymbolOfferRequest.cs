namespace ThriveDevCenter.Shared.Forms
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class DebugSymbolOfferRequest
    {
        [Required]
        [MaxLength(AppInfo.MaxDebugSymbolOfferBatch)]
        [MinLength(1)]
        public List<string> SymbolPaths { get; set; }
    }
}
