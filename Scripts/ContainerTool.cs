namespace Scripts;

using System;
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
        _ => throw new InvalidOperationException("Unknown image type"),
    };

    protected override string ImagesAndConfigsFolder => "./";

    protected override string DefaultImageToBuild => options.Image switch
    {
        ImageType.CI => "docker_ci",
        _ => throw new InvalidOperationException("Unknown image type"),
    };

    protected override string ImageNameBase => $"thrive/{ExportFileNameBase}";

    protected override Task<bool> PostCheckBuild(string tagOrId)
    {
        return CheckDotnetSdkWasInstalled(tagOrId);
    }
}
