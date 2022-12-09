namespace ThriveDevCenter.Server.Utilities;

using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

/// <summary>
///   Changes [Required] to work like [BindRequired].
///   Makes it cleaner to specify the POST form models for controllers.
/// </summary>
public class RequiredBindingMetadataProvider : IBindingMetadataProvider
{
    public void CreateBindingMetadata(BindingMetadataProviderContext context)
    {
        if (context.PropertyAttributes != null && context.PropertyAttributes.OfType<RequiredAttribute>().Any())
        {
            context.BindingMetadata.IsBindingRequired = true;
        }
    }
}
