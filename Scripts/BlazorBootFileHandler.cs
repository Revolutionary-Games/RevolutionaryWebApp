namespace Scripts;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SharedBase.Utilities;

public static class BlazorBootFileHandler
{
    private const string ShaPrefix = "sha256-";

    public static async Task FixBootJSONHashes(string file, CancellationToken cancellationToken)
    {
        var fileContent = await File.ReadAllTextAsync(file, cancellationToken);

        var data = (JsonObject?)JsonNode.Parse(fileContent);

        if (data == null)
            throw new ArgumentException($"Failed to load json object from {file}");

        if (await ProcessHashHelper(Path.GetDirectoryName(file) ?? throw new Exception("failed to get directory name"),
                data, cancellationToken))
        {
            Console.WriteLine($"Detected invalid hashes in {file}, recreating it");

            await using (var stream = File.Open(file, FileMode.Create, FileAccess.Write))
            {
                await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
                {
                    Indented = true,
                });

                data.WriteTo(writer);
            }

            await RegenerateCompressedFiles(file, cancellationToken);
        }
    }

    public static async Task RegenerateCompressedFiles(string file, CancellationToken cancellationToken)
    {
        var gzipped = $"{file}.gz";
        if (File.Exists(gzipped))
            File.Delete(gzipped);

        var content = await File.ReadAllBytesAsync(file, cancellationToken);
        var fileAttributes = new FileInfo(file);

        await using (var stream = File.Open(gzipped, FileMode.Create, FileAccess.Write))
        {
            await using var compressedStream = new GZipStream(stream, CompressionLevel.Optimal);

            await compressedStream.WriteAsync(content, 0, content.Length, cancellationToken);
        }

        var gzippedFileAttributes = new FileInfo(gzipped);
        gzippedFileAttributes.LastWriteTime = fileAttributes.LastWriteTime;
        gzippedFileAttributes.LastAccessTime = fileAttributes.LastAccessTime;

        var brotliProcessed = $"{file}.br";
        if (File.Exists(brotliProcessed))
            File.Delete(brotliProcessed);

        await using (var stream = File.Open(brotliProcessed, FileMode.Create, FileAccess.Write))
        {
            await using var compressedStream = new BrotliStream(stream, CompressionLevel.SmallestSize);

            await compressedStream.WriteAsync(content, 0, content.Length, cancellationToken);
        }

        var brotliFileAttributes = new FileInfo(brotliProcessed);
        brotliFileAttributes.LastWriteTime = fileAttributes.LastWriteTime;
        brotliFileAttributes.LastAccessTime = fileAttributes.LastAccessTime;
    }

    private static async Task<bool> ProcessHashHelper(string baseFolder, JsonObject jsonObject,
        CancellationToken cancellationToken)
    {
        bool changes = false;

        foreach (var property in jsonObject.ToList())
        {
            // Skip descending into unwanted data
            if (property.Key is "runtime" or "runtimeAssets")
                continue;

            switch (property.Value)
            {
                case JsonObject childObject:
                {
                    if (await ProcessHashHelper(baseFolder, childObject, cancellationToken))
                        changes = true;
                    break;
                }

                case JsonValue childValue when childValue.TryGetValue(out string? stringValue):
                {
                    if (!string.IsNullOrEmpty(stringValue) && stringValue.Contains(ShaPrefix))
                    {
                        var correct =
                            await CalculateFileSHa256Base64(Path.Join(baseFolder, property.Key), cancellationToken);

                        if (correct != stringValue)
                        {
                            Console.WriteLine($"Invalid hash detected for entry: {property.Key}");
                            jsonObject[property.Key] = JsonValue.Create(correct);
                            changes = true;
                        }
                    }

                    break;
                }
            }
        }

        return changes;
    }

    private static async Task<string> CalculateFileSHa256Base64(string file, CancellationToken cancellationToken)
    {
        return ShaPrefix + Convert.ToBase64String(await FileUtilities.CalculateSha256OfFile(file, cancellationToken));
    }
}
