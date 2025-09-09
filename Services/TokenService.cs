using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


namespace BotApp.Services
{
    public class TokenService
    {
        private readonly IConfiguration _cfg;
        public TokenService(IConfiguration cfg) => _cfg = cfg;

        public string IssueWebToken(Guid sessionId, string channelUserId)
        {
            var jwt = _cfg.GetSection("Jwt");
            var secret = jwt["Secret"] ?? throw new InvalidOperationException("Jwt:Secret missing");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, channelUserId),
            new("sid", sessionId.ToString()),
            new("channel", "web")
        };

            var expiry = DateTime.UtcNow.AddMinutes(int.Parse(jwt["ExpiryMinutes"] ?? "15"));

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: expiry,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
