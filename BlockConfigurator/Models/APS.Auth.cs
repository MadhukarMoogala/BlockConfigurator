using Autodesk.Authentication.Model;
using Autodesk.Authentication;

namespace BlockConfigurator.Models
{
    public record Token(string AccessToken, DateTime ExpiresAt);
    public partial class APS
    {
        private Token? _internalTokenCache;
        private Token? _publicTokenCache;

        private async Task<Token> GetToken(List<Scopes> scopes)
        {
            var authenticationClient = new AuthenticationClient(_sdkManager);
            var auth = await authenticationClient.GetTwoLeggedTokenAsync(_clientId, _clientSecret, scopes);
            if(auth.Equals(null) || auth.AccessToken.Equals(null) || auth.ExpiresIn.Equals(null))
            {
                throw new ApplicationException("Failed to authenticate with Autodesk.");
            }
            return new Token(auth.AccessToken, DateTime.UtcNow.AddSeconds((double)auth.ExpiresIn));
        }

        public async Task<Token> GetPublicToken()
        {
            if (_publicTokenCache == null || _publicTokenCache.ExpiresAt < DateTime.UtcNow)
                _publicTokenCache = await GetToken(new List<Scopes> { Scopes.ViewablesRead });
            return _publicTokenCache;
        }

        public async Task<Token> GetInternalToken()
        {
            if (_internalTokenCache == null || _internalTokenCache.ExpiresAt < DateTime.UtcNow)
                _internalTokenCache = await GetToken(new List<Scopes> { Scopes.BucketCreate, Scopes.BucketRead, Scopes.DataRead, Scopes.DataWrite, Scopes.DataCreate, Scopes.CodeAll});
            return _internalTokenCache;
        }

        public async Task<Token> DeleteBucketToken()
        {
            if (_internalTokenCache == null || _internalTokenCache.ExpiresAt < DateTime.UtcNow)
                _internalTokenCache = await GetToken([Scopes.BucketCreate, Scopes.BucketRead, Scopes.BucketDelete, Scopes.BucketUpdate, Scopes.DataRead, Scopes.DataWrite, Scopes.DataCreate, Scopes.DataSearch]);
            return _internalTokenCache;
        }
    }
}
