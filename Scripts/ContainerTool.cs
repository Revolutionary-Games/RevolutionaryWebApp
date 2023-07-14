namespace Scripts;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ScriptsBase.ToolBases;
using ScriptsBase.Utilities;

public class ContainerTool : ContainerToolBase<Program.ContainerOptions>
{
    public ContainerTool(Program.ContainerOptions options) : base(options)
    {
        ColourConsole.WriteInfoLine($"Selected image type to build: {options.Image}");
    }

    protected override string ExportFileNameBase => options.Image switch
    {
        ImageType.CI => "devcenter-ci",
        ImageType.Builder => "devcenter-builder",
        _ => throw new InvalidOperationException("Unknown image type"),
    };

    protected override string ImagesAndConfigsFolder => "./";

    protected override (string BuildRelativeFolder, string? TargetToStopAt) DefaultImageToBuild => options.Image switch
    {
        ImageType.CI => ("docker_ci", null),
        ImageType.Builder => (".", "builder"),
        _ => throw new InvalidOperationException("Unknown image type"),
    };

    protected override string ImageNameBase => $"thrive/{ExportFileNameBase}";

    protected override Task<bool> PostCheckBuild(string tagOrId)
    {
        return CheckDotnetSdkWasInstalled(tagOrId);
    }

    protected override IEnumerable<string> ImagesToPullIfTheyAreOld()
    {
        if (options.Image == ImageType.Builder)
        {
            // ReSharper disable once StringLiteralTypo
            yield return "rockylinux:9";
        }
    }
}
