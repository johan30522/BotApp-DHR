using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;


namespace BotApp.Helpers

{
    public class JwtSecurityTokenHelper
    {
        public static ClaimsPrincipal Validate(string jwt, IConfiguration cfg, out SecurityToken? validatedToken)
        {
            if (string.IsNullOrWhiteSpace(jwt))
                throw new SecurityTokenException("Empty token");

            var jwtCfg = cfg.GetSection("Jwt");
            var issuer = jwtCfg["Issuer"];
            var audience = jwtCfg["Audience"];
            var secret = jwtCfg["Secret"];

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret!)),
                ClockSkew = TimeSpan.FromSeconds(30)
            };

            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(jwt, parameters, out validatedToken);
        }
    }
}

