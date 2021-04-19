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
    using Microsoft.AspNetCore.Http;

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

        public async Task<long> GetObjectSize(string path)
        {
            ThrowIfNotConfigured();

            var data = await s3Client.GetObjectMetadataAsync(bucket, path);

            if(data.HttpStatusCode != HttpStatusCode.OK)
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

            var deleteResult = await s3Client.DeleteObjectAsync(new DeleteObjectRequest()
            {
                BucketName = bucket,
                Key = currentPath
            });

            if(deleteResult.HttpStatusCode != HttpStatusCode.OK)
                throw new Exception($"s3 object delete failed, status: {deleteResult.HttpStatusCode}");
        }

        /// <summary>
        ///   Gets object content as a stream. Note the result needs to be disposed
        /// </summary>
        public async Task<Stream> GetObjectContent(string path)
        {
            ThrowIfNotConfigured();

            var result =  await s3Client.GetObjectAsync(new GetObjectRequest()
            {
                BucketName = bucket,
                Key = path
            });

            if(result.HttpStatusCode != HttpStatusCode.OK)
                throw new Exception($"s3 object content retrieve failed, status: {result.HttpStatusCode}");

            return result.ResponseStream;
        }

        public async Task DeleteObject(string path)
        {
            await s3Client.DeleteObjectAsync(new DeleteObjectRequest()
            {
                BucketName = bucket,
                Key = path
            });
        }

        public async Task<string> ComputeSha256OfObject(string path)
        {
            await using var dataStream = await GetObjectContent(path);

            var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(dataStream);

            return Convert.ToHexString(hash).ToLowerInvariant();
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
