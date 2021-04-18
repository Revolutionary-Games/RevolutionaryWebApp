namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Threading.Tasks;
    using Amazon;
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
            return data.Headers.ContentLength;
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
