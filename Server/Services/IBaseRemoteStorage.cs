namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using DevCenterCommunication.Models;
using Filters;
using ICSharpCode.SharpZipLib.GZip;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models;
using SHA3.Net;
using Utilities;

public interface IBaseRemoteStorage : IDisposable
{
    public bool Configured { get; }
    public Task<bool> BucketExists();
    public string CreatePresignedUploadURL(string path, TimeSpan expiresIn);
    public Task UploadText(string path, string data);
    public Task UploadFile(string path, Stream data, string contentType, CancellationToken cancellationToken);

    public string CreatePreSignedDownloadURL(string path, TimeSpan expiresIn);
    public string CreatePresignedPostURL(string path, string mimeType, TimeSpan expiresIn);
    public Task<long> GetObjectSize(string path);
    public Task MoveObject(string currentPath, string newPath);

    /// <summary>
    ///   Gets object content as a stream. Note the result needs to be disposed
    /// </summary>
    public Task<Stream> GetObjectContent(string path);

    public Task DeleteObject(string path);
    public Task<string> ComputeSha256OfObject(string path);
    public Task<string> ComputeSha3OfObject(string path);
    public Task<string> ComputeSha3UnZippedObject(string path);

    public Task<StorageFile> HandleFinishedUploadToken(ApplicationDbContext database,
        StorageUploadVerifyToken token);

    public Task PerformFileUploadSuccessActions(StorageFile file, ApplicationDbContext database);
    public bool TokenMatchesFile(StorageUploadVerifyToken token, StorageFile file);
}

public abstract class BaseRemoteStorage : IBaseRemoteStorage
{
    private readonly AmazonS3Client? s3Client;
    private readonly string bucket;

    protected BaseRemoteStorage(string? region, string? endpoint, string? accessKeyId, string? secretAccessKey,
        string? bucketName)
    {
        bucket = bucketName ?? string.Empty;

        if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(bucketName))
        {
            Configured = false;
            return;
        }

        if (accessKeyId == null || secretAccessKey == null)
        {
            Configured = false;
            return;
        }

        s3Client = new AmazonS3Client(new BasicAWSCredentials(accessKeyId, secretAccessKey), new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = region,
        });

        Configured = true;
    }

    public bool Configured { get; }

    public Task<bool> BucketExists()
    {
        ThrowIfNotConfigured();
        return AmazonS3Util.DoesS3BucketExistV2Async(s3Client, bucket);
    }

    public string CreatePresignedUploadURL(string path, TimeSpan expiresIn)
    {
        ThrowIfNotConfigured();

        return s3Client!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = path,
            Expires = DateTime.UtcNow + expiresIn,
            Verb = HttpVerb.PUT,
        });
    }

    public async Task<string> CreateMultipartUpload(string path, string mimeType)
    {
        ThrowIfNotConfigured();

        var response = await s3Client!.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = bucket,
            Key = path,
            ContentType = mimeType,
        });

        return response.UploadId;
    }

    /// <summary>
    ///   Creates a pre-signed upload URL that is part of a multipart upload
    /// </summary>
    public string CreatePresignedUploadURL(string path, string uploadId, int partNumber, TimeSpan expiresIn)
    {
        ThrowIfNotConfigured();

        return s3Client!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = path,
            Expires = DateTime.UtcNow + expiresIn,
            Verb = HttpVerb.PUT,
            UploadId = uploadId,
            PartNumber = partNumber,
        });
    }

    public async Task FinishMultipartUpload(string path, string uploadId, List<PartETag> parts)
    {
        ThrowIfNotConfigured();

        await s3Client!.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = bucket,
            Key = path,
            UploadId = uploadId,
            PartETags = parts,
        });
    }

    public async Task AbortMultipartUpload(string path, string uploadId)
    {
        ThrowIfNotConfigured();

        await s3Client!.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = bucket,
            Key = path,
            UploadId = uploadId,
        });
    }

    public async Task<List<PartDetail>> ListMultipartUploadParts(string path, string uploadId)
    {
        ThrowIfNotConfigured();

        var parts = new List<PartDetail>();

        string? continueParts = null;

        ListPartsResponse response;
        do
        {
            response = await s3Client!.ListPartsAsync(new ListPartsRequest
            {
                BucketName = bucket,
                Key = path,
                UploadId = uploadId,
                PartNumberMarker = continueParts,
            });

            parts.AddRange(response.Parts);
            continueParts = response.NextPartNumberMarker.ToString(CultureInfo.InvariantCulture);
        }
        while (response.IsTruncated);

        return parts;
    }

    public async Task<List<(string Key, string UploadId)>> ListMultipartUploads(CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        var parts = new List<(string Key, string UploadId)>();

        string? nextKeyMarker = null;
        string? nextUploadIdMarker = null;

        ListMultipartUploadsResponse response;

        do
        {
            response = await s3Client!.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
            {
                BucketName = bucket,
                KeyMarker = nextKeyMarker,
                UploadIdMarker = nextUploadIdMarker,
            }, cancellationToken);

            parts.AddRange(response.MultipartUploads.Select(u => (u.Key, u.UploadId)));

            nextKeyMarker = response.NextKeyMarker;
            nextUploadIdMarker = response.NextUploadIdMarker;
        }
        while (response.IsTruncated);

        return parts;
    }

    public Task UploadText(string path, string data)
    {
        ThrowIfNotConfigured();

        return s3Client!.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = path,
            ContentBody = data,
        });
    }

    public Task UploadFile(string path, Stream data, string contentType, CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        return s3Client!.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = path,
            InputStream = data,
            ContentType = contentType,
        }, cancellationToken);
    }

    public string CreatePreSignedDownloadURL(string path, TimeSpan expiresIn)
    {
        ThrowIfNotConfigured();

        return s3Client!.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = path,
            Expires = DateTime.UtcNow + expiresIn,
            Verb = HttpVerb.GET,
        });
    }

    public string CreatePresignedPostURL(string path, string mimeType, TimeSpan expiresIn)
    {
        ThrowIfNotConfigured();

        // Sadly not supported by the C# SDK
        /*
                bucket.object(remote_path).presigned_post(
                signature_expiration: Time.now + UPLOAD_EXPIRE_TIME + 1, key: remote_path,
                content_type: client_mime_type,
                content_length_range: 1..MAX_ALLOWED_REMOTE_OBJECT_SIZE
                )
         */

        throw new NotImplementedException();
    }

    public async Task<long> GetObjectSize(string path)
    {
        ThrowIfNotConfigured();

        var data = await s3Client!.GetObjectMetadataAsync(bucket, path);

        if (data.HttpStatusCode != HttpStatusCode.OK)
            throw new Exception($"s3 object size get failed: {data.HttpStatusCode}");

        return data.Headers.ContentLength;
    }

    public async Task MoveObject(string currentPath, string newPath)
    {
        ThrowIfNotConfigured();

        var copyResult = await s3Client!.CopyObjectAsync(new CopyObjectRequest
        {
            SourceBucket = bucket,
            SourceKey = currentPath,
            DestinationBucket = bucket,
            DestinationKey = newPath,
        });

        if (copyResult.HttpStatusCode != HttpStatusCode.OK)
            throw new Exception($"s3 object copy failed, status: {copyResult.HttpStatusCode}");

        await DeleteObject(currentPath);
    }

    /// <summary>
    ///   Gets object content as a stream. Note the result needs to be disposed
    /// </summary>
    public async Task<Stream> GetObjectContent(string path)
    {
        ThrowIfNotConfigured();

        var result = await s3Client!.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucket,
            Key = path,
        });

        if (result.HttpStatusCode != HttpStatusCode.OK)
            throw new Exception($"s3 object content retrieve failed, status: {result.HttpStatusCode}");

        return result.ResponseStream;
    }

    public async Task DeleteObject(string path)
    {
        var deleteResult = await s3Client!.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucket,
            Key = path,
        });

        if (deleteResult.HttpStatusCode != HttpStatusCode.NoContent &&
            deleteResult.HttpStatusCode != HttpStatusCode.OK)
            throw new Exception($"s3 object delete failed, status: {deleteResult.HttpStatusCode}");
    }

    public async Task<IEnumerable<string>> ListFirstThousandFiles(CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        var response = await s3Client!.ListObjectsAsync(new ListObjectsRequest
        {
            BucketName = bucket,
        }, cancellationToken);

        return response.S3Objects.Select(o => o.Key);
    }

    /// <summary>
    ///   Lists all files in a bucket. Note that this uses the list V2 API and may not be supported
    /// </summary>
    /// <param name="cancellationToken">Can be used to cancel</param>
    /// <returns>List of existing object paths</returns>
    public async Task<IEnumerable<string>> ListAllFiles(CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        var files = new List<string>();

        string? nextContinuationToken = null;

        ListObjectsV2Response response;

        do
        {
            response = await s3Client!.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucket,
                ContinuationToken = nextContinuationToken,
            }, cancellationToken);

            files.AddRange(response.S3Objects.Select(o => o.Key));

            nextContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated);

        return files;
    }

    public async Task<string> ComputeSha256OfObject(string path)
    {
        await using var dataStream = await GetObjectContent(path);

        var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(dataStream);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<string> ComputeSha3OfObject(string path)
    {
        await using var dataStream = await GetObjectContent(path);

        var sha3 = Sha3.Sha3256();
        var hash = await sha3.ComputeHashAsync(dataStream);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<string> ComputeSha3UnZippedObject(string path)
    {
        await using var dataStream = await GetObjectContent(path);

        var sha3 = Sha3.Sha3256();

        await using var unzipped = new GZipInputStream(dataStream);

        var hash = await sha3.ComputeHashAsync(unzipped);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<StorageFile> HandleFinishedUploadToken(ApplicationDbContext database,
        StorageUploadVerifyToken token)
    {
        var file = await database.StorageFiles.Include(f => f.StorageItemVersions)
            .ThenInclude(v => v.StorageItem).FirstOrDefaultAsync(f => f.Id == token.FileId);

        if (file == null)
            throw new Exception("StorageFile not found");

        if (!TokenMatchesFile(token, file))
            throw new Exception("Token doesn't match file");

        if (!file.Uploading)
            throw new Exception("File is already marked as uploaded");

        if (file.StorageItemVersions.Count < 1)
            throw new Exception("Uploaded StorageFile has no associated item version object(s)");

        foreach (var version in file.StorageItemVersions)
        {
            if (!version.Uploading)
                throw new Exception("Can't use token on item that has already uploaded version object");
        }

        // Verify that upload to S3 was successful
        long actualSize;

        try
        {
            actualSize = await GetObjectSize(file.UploadPath);
        }
        catch (Exception e)
        {
            throw new Exception("Checking item size in remote storage failed", e);
        }

        if (actualSize != file.Size)
            throw new Exception($"File size in storage doesn't match expected. {actualSize} != {file.Size}");

        // Move file to the actual target location
        await MoveObject(file.UploadPath, file.StoragePath);

        return file;
    }

    public async Task PerformFileUploadSuccessActions(StorageFile file, ApplicationDbContext database)
    {
        file.BumpUpdatedAt();
        file.Uploading = false;

        foreach (var version in file.StorageItemVersions)
        {
            version.Uploading = false;
            version.BumpUpdatedAt();

            // Update StorageItem if the version is the latest
            if (version.Version >= await database.StorageItemVersions
                    .Where(s => s.StorageItemId == version.StorageItemId).MaxAsync(s => s.Version))
            {
                if (version.StorageItem == null)
                    throw new NotLoadedModelNavigationException();

                version.StorageItem.Size = file.Size;
                version.StorageItem.BumpUpdatedAt();
            }
        }
    }

    public bool TokenMatchesFile(StorageUploadVerifyToken token, StorageFile file)
    {
        return token.FileStoragePath == file.StoragePath && token.FileUploadPath == file.UploadPath &&
            token.FileSize == file.Size && token.FileId == file.Id;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            s3Client?.Dispose();
        }
    }

    protected void ThrowIfNotConfigured()
    {
        if (!Configured || s3Client == null)
        {
            throw new HttpResponseException
                { Status = StatusCodes.Status500InternalServerError, Value = "Remote storage is not configured" };
        }
    }
}
