using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using DevCenterCommunication.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Models;

/// <summary>
///   Returns the info about Thrive and launcher versions the launcher needs to download, edited through
///   <see cref="LauncherInfoConfigurationController"/>
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class LauncherInfoController : Controller
{
    private readonly ILogger<LauncherInfoController> logger;
    private readonly IConfiguration configuration;
    private readonly ApplicationDbContext database;
    private readonly string? signingCertFile;
    private readonly string? signingCertPassword;

    public LauncherInfoController(ILogger<LauncherInfoController> logger, IConfiguration configuration,
        ApplicationDbContext database)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.database = database;

        signingCertFile = configuration["Launcher:InfoSigningKey"];
        signingCertPassword = configuration["Launcher:InfoSigningKeyPassword"];

        if (string.IsNullOrEmpty(signingCertFile) && !string.IsNullOrEmpty(signingCertPassword))
            logger.LogWarning("Signing cert password is specified but no file is defined");
    }

    [NonAction]
    public static async Task<LauncherThriveInformation> GenerateLauncherInfoObject(ApplicationDbContext database,
        IConfiguration configuration)
    {
        var launcherDownloads = new Uri(configuration["Launcher:LauncherDownloadsPage"]);

        return new LauncherThriveInformation(new LauncherVersionInfo("2.0.0")
            {
                DownloadsPage = launcherDownloads,
            }, 27,
            new List<ThriveVersionLauncherInfo>
            {
                new(27, "0.5.10", new Dictionary<PackagePlatform, DownloadableInfo>
                {
                    {
                        PackagePlatform.Linux, new DownloadableInfo(
                            "7c9137ed64dc7e0d8c93113b90b79f84a63d85b2e8b824e9554a2f4457d72399",
                            "Thrive_0.5.10.0_linux_x11",
                            new Dictionary<string, Uri>
                            {
                                {
                                    "github",
                                    new Uri(
                                        "https://github.com/Revolutionary-Games/Thrive/releases/download/v0.5.10/" +
                                        "Thrive_0.5.10.0_linux_x11.7z")
                                },
                            })
                    },
                })
                {
                    Stable = true,
                },
                new(26, "0.5.9", new Dictionary<PackagePlatform, DownloadableInfo>
                {
                    {
                        PackagePlatform.Linux, new DownloadableInfo(

                            // "827304db6b306a2e16b61250f4f3152ec03f05a0eb06bc6305259be20a49727f",
                            // Intentionally wrong hash:
                            "abc1234db6b306a2e16b61250f4f3152ec03f05a0eb06bc6305259be20a49727f",
                            "Thrive_0.5.9.0_linux_x11",
                            new Dictionary<string, Uri>
                            {
                                {
                                    "github",
                                    new Uri(
                                        "https://github.com/Revolutionary-Games/Thrive/releases/download/v0.5.9/" +
                                        "Thrive_0.5.9.0_linux_x11.7z")
                                },
                            })
                    },
                })
                {
                    Stable = true,
                },
            }, new Dictionary<string, DownloadMirrorInfo>
            {
                { "github", new DownloadMirrorInfo(new Uri("https://github.com"), "Github") },
            });
    }

    [HttpGet]
    [ResponseCache(Duration = 600)]
    public async Task<ActionResult<Stream>> GetInfoForLauncher()
    {
        var info = GenerateLauncherInfoObject(database, configuration);

        using var compressedDataStream = new MemoryStream();

        {
            using var dataStream = new MemoryStream();

            await JsonSerializer.SerializeAsync(dataStream, info);

            dataStream.Position = 0;

            await using var compressor = new BrotliStream(compressedDataStream, CompressionLevel.Optimal, true);

            await dataStream.CopyToAsync(compressor);
        }

        if (compressedDataStream.Length >= int.MaxValue)
            throw new Exception("Generated data is too long");

        var signatureHandler = new SignedDataHandler();

        compressedDataStream.Position = 0;

        byte[] signature;

        if (!string.IsNullOrEmpty(signingCertFile))
        {
            signature = await signatureHandler.CreateSignature(compressedDataStream, signingCertFile,
                signingCertPassword);
            compressedDataStream.Position = 0;
        }
        else
        {
            logger.LogWarning("Not signing the launcher info, this will only work for local testing");
            signature = Encoding.UTF8.GetBytes("no key specified (testing use only)");
        }

        var finalContent = new MemoryStream();

        await signatureHandler.WriteDataWithSignature(finalContent, compressedDataStream, signature);

        finalContent.Position = 0;
        HttpContext.Response.ContentType = "application/octet-stream";

        return finalContent;
    }
}
