namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Shared.Models.Enums;
using SharedBase.Utilities;

public class Stackwalk : IStackwalk
{
    private const int PrimaryCallstackSubstituteCharacterCount = 400;
    private const int MaximumPrimaryCallstackLength = 15000;

    private static readonly Regex CrashedThreadRegex =
        new(@"Thread\s+\d+\s+\(crashed\).*", RegexOptions.IgnoreCase);

    private static readonly Regex StackFrameStartRegex = new(@"^\s*\d+\s+.*");
    private static readonly Regex NoFramesRegex = new(@"^\s*<no\s+frames>.*");

    private readonly HttpClient httpClient;
    private readonly Uri? serviceBaseUrl;

    public Stackwalk(IConfiguration configuration)
    {
        var url = configuration["Crashes:StackwalkService"];

        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        if (string.IsNullOrEmpty(url))
            return;

        serviceBaseUrl = new Uri(url);

        Configured = true;
    }

    public bool Configured { get; }

    public Task<string> PerformBlockingStackwalk(string dumpFilePath, ThrivePlatform platform,
        CancellationToken cancellationToken)
    {
        return PerformBlockingStackwalk(File.OpenRead(dumpFilePath), platform, cancellationToken);
    }

    public async Task<string> PerformBlockingStackwalk(Stream dumpContent, ThrivePlatform platform,
        CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        string stackwalkMode;
        switch (platform)
        {
            case ThrivePlatform.Windows:
                stackwalkMode = "mingw";
                break;
            default:
                stackwalkMode = "normal";
                break;
        }

        var url = new Uri(serviceBaseUrl!,
            QueryHelpers.AddQueryString("api/v1",
                new Dictionary<string, string?> { { "stackwalkType", stackwalkMode } }));

        using var form = new MultipartFormDataContent();

        form.Add(new StreamContent(dumpContent), "file", "file");

        // Note that due to using POST here we can't use HttpCompletionOption.ResponseHeadersRead so we just need to
        // deal with the fact that the response up to a few megabytes gets buffered here
        var response = await httpClient.PostAsync(url, form, cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception($"Stackwalk service responded with unexpected status code: {response.StatusCode}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        await using var rawReader = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(rawReader, Encoding.UTF8);

        return await reader.ReadToEndAsync();
    }

    public string? FindPrimaryCallstack(string decodedDump, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(decodedDump))
            return null;

        var builder = new StringBuilder(500);

        bool foundStart = false;

        foreach (var line in decodedDump.Split('\n'))
        {
            if (foundStart)
            {
                if (string.IsNullOrWhiteSpace(line))
                    break;

                builder.Append(line);
                builder.Append('\n');
            }
            else if (CrashedThreadRegex.IsMatch(line))
            {
                foundStart = true;
                builder.Append(line);
                builder.Append('\n');
            }
        }

        if (!foundStart)
        {
            if (fallback)
                return decodedDump.Truncate(PrimaryCallstackSubstituteCharacterCount);

            return null;
        }

        return builder.ToString().Truncate(MaximumPrimaryCallstackLength);
    }

    public string? CondenseCallstack(string? callstack)
    {
        if (callstack == null)
            return null;

        var builder = new StringBuilder(500);

        foreach (var line in callstack.Split('\n'))
        {
            if (NoFramesRegex.IsMatch(line))
            {
                // No frames reported for this callstack, just copy the current line and end
                builder.Append(line);
                builder.Append('\n');
                break;
            }

            if (!StackFrameStartRegex.IsMatch(line))
                continue;

            builder.Append(line);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private void ThrowIfNotConfigured()
    {
        if (!Configured)
            throw new InvalidOperationException("Stackwalk service is not configured");
    }
}

public interface IStackwalk
{
    bool Configured { get; }

    Task<string> PerformBlockingStackwalk(string dumpFilePath, ThrivePlatform platform,
        CancellationToken cancellationToken);

    Task<string> PerformBlockingStackwalk(Stream dumpContent, ThrivePlatform platform,
        CancellationToken cancellationToken);

    /// <summary>
    ///   Tries to find the primary (crashing thread) callstack from a stackwalk decoded crash dump
    /// </summary>
    /// <param name="decodedDump">
    ///   The stackwalk decoded output, for example from
    ///   <see cref="PerformBlockingStackwalk(string, ThrivePlatform, CancellationToken)"/>
    /// </param>
    /// <param name="fallback">
    ///   If true then first few hundred characters are considered the primary callstack, if searching for it
    ///   failed
    /// </param>
    /// <returns>The found primary callstack or null</returns>
    string? FindPrimaryCallstack(string decodedDump, bool fallback = true);

    /// <summary>
    ///   Takes in a callstack with register and other info present and condenses it
    /// </summary>
    /// <param name="callstack">
    ///     The callstack to process, for example from <see cref="FindPrimaryCallstack"/>.
    ///     If null, null is returned.
    /// </param>
    /// <returns>The condensed callstack with just the function and location information</returns>
    string? CondenseCallstack(string? callstack);
}
