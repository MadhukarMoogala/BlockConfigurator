using BlockConfigurator.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlockConfigurator.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        public record AccessToken(string access_token, long expires_in);

        private readonly APS _aps;

        public AuthController(APS aps)
        {
            _aps = aps;
        }

        [HttpGet("token")]
        public async Task<AccessToken> GetAccessToken()
        {
            var token = await _aps.GetPublicToken();
            return new AccessToken(
                token.AccessToken,
                (long)Math.Round((token.ExpiresAt - DateTime.UtcNow).TotalSeconds)
            );
        }
    }
}
