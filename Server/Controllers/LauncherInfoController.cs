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
///   Returns the info about Thrive and launcher versions the launcher needs to download, and also modifying that info
///   by an admin
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class LauncherInfoController : Controller
{
    private readonly ILogger<LauncherInfoController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly string? signingCertFile;
    private readonly string? signingCertPassword;

    public LauncherInfoController(ILogger<LauncherInfoController> logger, IConfiguration configuration,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;

        signingCertFile = configuration["Launcher:InfoSigningKey"];
        signingCertPassword = configuration["Launcher:InfoSigningKeyPassword"];

        if (string.IsNullOrEmpty(signingCertFile) && !string.IsNullOrEmpty(signingCertPassword))
            logger.LogWarning("Signing cert password is specified but no file is defined");
    }

    [HttpGet]
    [ResponseCache(Duration = 600)]
    public async Task<ActionResult<Stream>> GetInfoForLauncher()
    {
        var info = new LauncherThriveInformation(new LauncherVersionInfo("2.0.0"), 26,
            new List<ThriveVersionLauncherInfo>()
            {
                new(26, "0.5.10", new Dictionary<PackagePlatform, DownloadableInfo>()
                {
                    {
                        PackagePlatform.Linux, new DownloadableInfo("1234", new Dictionary<string, Uri>()
                        {
                            {
                                "github",
                                new Uri(
                                    "https://github.com/Revolutionary-Games/Thrive/releases/download/v0.5.9/Thrive_0.5.9.0_linux_x11.7z")
                            },
                        })
                    },
                }),
            }, new Dictionary<string, DownloadMirrorInfo>()
            {
                { "github", new DownloadMirrorInfo(new Uri("https://github.com"), "Github") },
            });

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
