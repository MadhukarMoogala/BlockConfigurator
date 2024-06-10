using Autodesk.SDKManager;

namespace BlockConfigurator.Models
{
    public partial class APS
    {
        private readonly SDKManager _sdkManager;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _bucket;
        public string GetClientId() => _clientId;
        public string GetBucketKey() => _bucket;
        public APS(string clientId, string clientSecret, string bucket)
        {
            _sdkManager = SdkManagerBuilder.Create().Build();
            _clientId = clientId;
            _clientSecret = clientSecret;
            _bucket = string.IsNullOrEmpty(bucket) ? $"blkconfig_135052024" : bucket;
            CleanUp().Wait();
        }
    }
}
