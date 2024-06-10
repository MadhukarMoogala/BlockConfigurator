using Autodesk.Oss.Model;
using Autodesk.Oss;

namespace BlockConfigurator.Models
{
    public sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile() :
          this(Path.GetTempPath())
        { }

        public TemporaryFile(string directory)
        {
            Create(Path.Combine(directory, Path.GetRandomFileName()));
        }

        ~TemporaryFile()
        {
            Delete();
        }

        public void Dispose()
        {
            Delete();
            GC.SuppressFinalize(this);
        }

        public string? FilePath { get; private set; }

        private void Create(string path)
        {
            FilePath = path;
            using (File.Create(FilePath)) { };
        }

        private void Delete()
        {
            if (FilePath == null) return;
            File.Delete(FilePath);
            FilePath = null;
        }
    }
    public partial class APS
    {
        private async Task EnsureBucketExists(string bucketKey)
        {
            const string region = "US";
            var auth = await GetInternalToken();
            var ossClient = new OssClient(_sdkManager);
            try
            {
                await ossClient.GetBucketDetailsAsync(bucketKey, accessToken: auth.AccessToken);
            }
            catch (OssApiException ex)
            {
                if (ex.HttpResponseMessage.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var payload = new CreateBucketsPayload
                    {
                        BucketKey = bucketKey,
                        PolicyKey = "transient"
                    };
                    await ossClient.CreateBucketAsync(region, payload, auth.AccessToken);
                }
                else
                {
                    throw;
                }
            }
        }

        public async Task<ObjectDetails> UploadModel(string objectName, string pathToFile)
        {
            await EnsureBucketExists(_bucket);
            var auth = await GetInternalToken();
            var ossClient = new OssClient(_sdkManager);
            var objectDetails = await ossClient.Upload(_bucket, objectName, pathToFile, auth.AccessToken, new CancellationToken());
            return objectDetails;
        }

        public async Task<IEnumerable<ObjectDetails>> GetObjects()
        {
            await EnsureBucketExists(_bucket);
            var auth = await GetInternalToken();
            var ossClient = new OssClient(_sdkManager);
            const int PageSize = 64;
            var results = new List<ObjectDetails>();
            var response = await ossClient.GetObjectsAsync(_bucket, PageSize, accessToken: auth.AccessToken);
            results.AddRange(response.Items);
            while (!string.IsNullOrEmpty(response.Next))
            {
                var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(response.Next).Query);
                response = await ossClient.GetObjectsAsync(_bucket, PageSize, startAt: queryParams["startAt"], accessToken: auth.AccessToken);
                results.AddRange(response.Items);
            }
            return results;
        }
        public async Task<ObjectDetails> GetObjectId(string objectKey, string temp, bool isEmpty)
        {
            await EnsureBucketExists(_bucket);
            var auth = await GetInternalToken();
            var ossClient = new OssClient(_sdkManager);
            ObjectDetails objectDetails;
            if (isEmpty && Directory.Exists(temp))
            {
                using (var tempFile = new TemporaryFile(temp))
                {
                    // use the file through tempFile.FilePath...
                    objectDetails = await ossClient.Upload(_bucket, objectKey, tempFile.FilePath, auth.AccessToken, new System.Threading.CancellationToken());
                }
            }
            else if (!isEmpty && File.Exists(temp))
            {
                objectDetails = await ossClient.Upload(_bucket, objectKey, temp, accessToken: auth.AccessToken, new System.Threading.CancellationToken());
            }
            else
            {
                throw new FileNotFoundException("File not found.");
            }
            return objectDetails;
        }
        public async Task<string> GetSignedS3DownloadLink(string objectKey)
        {
            await EnsureBucketExists(_bucket);
            var auth = await GetInternalToken();
            var ossClient = new OssClient(_sdkManager);
            var signedResponseBody = new CreateSignedResource()
            {
                MinutesExpiration = 15,
                SingleUse = false
            };

            var s3resp = await ossClient.CreateSignedResourceAsync(_bucket, objectKey, "read",
                                                                   useCdn: true, createSignedResource: signedResponseBody,
                                                                   accessToken: auth.AccessToken);
            return s3resp.SignedUrl;
        }

        public async Task CleanUp()
        {
            //delete all buckets
            var auth = await DeleteBucketToken();
            var ossClient = new OssClient(_sdkManager);
            try
            {
               
                var bucketObjects = await ossClient.GetObjectsAsync(GetBucketKey(),accessToken: auth.AccessToken);
                foreach (var item in bucketObjects.Items)
                {

                    var res = await ossClient.DeleteObjectAsync(item.BucketKey, item.ObjectKey, accessToken:auth.AccessToken, throwOnError: false);
                    if (res.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        Console.WriteLine($"Item {item.ObjectKey} deleted.");
                    }
                    else
                    {
                        continue;
                    }
                }
            }
            catch (OssApiException)
            {

                throw;
            }

        }
    }
}
