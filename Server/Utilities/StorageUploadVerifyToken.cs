namespace ThriveDevCenter.Server.Utilities;

using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

public class StorageUploadVerifyToken
{
    private readonly IDataProtector? dataProtector;

    /// <summary>
    ///   Constructs a new instance on the server that can be converted to string to be sent to a client
    /// </summary>
    public StorageUploadVerifyToken(IDataProtector dataProtector, string fileUploadPath, string fileStoragePath,
        long fileSize, long fileId, long? parentId, string? unGzippedHash, string? plainFileHash)
    {
        this.dataProtector = dataProtector;
        FileUploadPath = fileUploadPath;
        FileStoragePath = fileStoragePath;
        FileSize = fileSize;
        FileId = fileId;
        ParentId = parentId;
        UnGzippedHash = unGzippedHash;
        PlainFileHash = plainFileHash;
    }

    [JsonConstructor]
    public StorageUploadVerifyToken(string fileUploadPath, string fileStoragePath, long fileSize, long fileId)
    {
        FileUploadPath = fileUploadPath;
        FileStoragePath = fileStoragePath;
        FileSize = fileSize;
        FileId = fileId;
    }

    public string FileUploadPath { get; set; }
    public string FileStoragePath { get; set; }
    public long FileSize { get; set; }
    public long FileId { get; set; }
    public long? ParentId { get; set; }

    /// <summary>
    ///   This hash is done so that the remote file is ungzipped (in memory) when calculating the hash
    /// </summary>
    public string? UnGzippedHash { get; set; }

    /// <summary>
    ///   This hash is directly just the sha3 of the remote file
    /// </summary>
    public string? PlainFileHash { get; set; }

    /// <summary>
    ///   Place to store extra data in situations where <see cref="ParentId"/> (and other fields) are not enough. For
    ///   example when the upload is related to a model that doesn't have a primary ID but a composite key. Needs to be
    ///   really careful with the data format here to ensure there's no potential parsing bugs with user provided data.
    /// </summary>
    public string? ExtraDataStore { get; set; }

    public static StorageUploadVerifyToken? TryToLoadFromString(IDataProtector dataProtector, string tokenStr)
    {
        try
        {
            var unprotected = dataProtector.Unprotect(tokenStr);
            return JsonSerializer.Deserialize<StorageUploadVerifyToken>(unprotected);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            // TODO: logging for this? Quite unlikely that this can happen, but might be nice to try to log this
            return null;
        }
    }

    public override string ToString()
    {
        // We don't want to have to deal with nulls up the chain, only with problematic base ToString methods will we
        // throw an exception
#pragma warning disable CA1065
        if (dataProtector == null)
            return base.ToString() ?? throw new InvalidOperationException("base ToString returned null");
#pragma warning restore CA1065

        return dataProtector.Protect(JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        }));
    }
}
