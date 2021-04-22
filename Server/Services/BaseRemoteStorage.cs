namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.IO;
    using System.Net;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.S3.Util;
    using Filters;
    using ICSharpCode.SharpZipLib.GZip;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using SHA3.Net;
    using Utilities;

    public abstract class BaseRemoteStorage
    {
        private readonly AmazonS3Client s3Client;
        private readonly string bucket;

        protected BaseRemoteStorage(string region, string endpoint, string accessKeyId, string secretAccessKey,
            string bucketName)
        {
            if (string.IsNullOrEmpty(region) || string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(bucketName))
            {
                Configured = false;
                return;
            }

            s3Client = new AmazonS3Client(new BasicAWSCredentials(accessKeyId, secretAccessKey), new AmazonS3Config()
            {
                ServiceURL = endpoint,
                AuthenticationRegion = region
            });

            bucket = bucketName;
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

            return s3Client.GetPreSignedURL(new GetPreSignedUrlRequest()
            {
                BucketName = bucket,
                Key = path,
                Expires = DateTime.UtcNow + expiresIn,
                Verb = HttpVerb.PUT
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

            var data = await s3Client.GetObjectMetadataAsync(bucket, path);

            if (data.HttpStatusCode != HttpStatusCode.OK)
                throw new Exception($"s3 object size get failed: {data.HttpStatusCode}");

            return data.Headers.ContentLength;
        }

        public async Task MoveObject(string currentPath, string newPath)
        {
            var copyResult = await s3Client.CopyObjectAsync(new CopyObjectRequest()
            {
                SourceBucket = bucket,
                SourceKey = currentPath,
                DestinationBucket = bucket,
                DestinationKey = newPath
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

            var result = await s3Client.GetObjectAsync(new GetObjectRequest()
            {
                BucketName = bucket,
                Key = path
            });

            if (result.HttpStatusCode != HttpStatusCode.OK)
                throw new Exception($"s3 object content retrieve failed, status: {result.HttpStatusCode}");

            return result.ResponseStream;
        }

        public async Task DeleteObject(string path)
        {
            var deleteResult = await s3Client.DeleteObjectAsync(new DeleteObjectRequest()
            {
                BucketName = bucket,
                Key = path
            });

            if (deleteResult.HttpStatusCode != HttpStatusCode.NoContent &&
                deleteResult.HttpStatusCode != HttpStatusCode.OK)
                throw new Exception($"s3 object delete failed, status: {deleteResult.HttpStatusCode}");
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

        public void MarkFileAndVersionsAsUploaded(StorageFile file)
        {
            file.Uploading = false;

            foreach (var version in file.StorageItemVersions)
                version.Uploading = false;
        }

        public bool TokenMatchesFile(StorageUploadVerifyToken token, StorageFile file)
        {
            return token.FileStoragePath == file.StoragePath && token.FileUploadPath == file.UploadPath &&
                token.FileSize == file.Size && token.FileId == file.Id;
        }

        protected void ThrowIfNotConfigured()
        {
            if (!Configured)
            {
                throw new HttpResponseException()
                    { Status = StatusCodes.Status500InternalServerError, Value = "Remote storage is not configured" };
            }
        }
    }
}
