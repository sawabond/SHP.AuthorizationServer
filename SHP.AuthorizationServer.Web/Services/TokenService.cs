using DAL.Entities;
using DAL.Interfaces;
using IdentityServer.Options;
using IdentityServer.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using SHP.AuthorizationServer.Web.Contracts;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace IdentityServer.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;
        private readonly TokenValidationParameters _tokenValidationParameters;
        private readonly IUnitOfWork _uow;
        private readonly SymmetricSecurityKey _key;

        public TokenService(
            IConfiguration configuration,
            TokenValidationParameters tokenValidationParameters,
            IUnitOfWork uow)
        {
            _config = configuration;
            _tokenValidationParameters = tokenValidationParameters;
            _uow = uow;
            _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config[ConfigurationOptions.Token]));
        }

        public async Task<AuthenticationResult> CreateToken(
            AppUser user,
            ICollection<string> roles,
            RefreshToken refreshToken = null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(1),
                SigningCredentials = creds
            };

            var tokenHandler = new JwtSecurityTokenHandler();

            var token = tokenHandler.CreateToken(tokenDescriptor);

            if (refreshToken is null)
            {
                refreshToken = new RefreshToken
                {
                    Token = Guid.NewGuid().ToString(),
                    JwtId = token.Id,
                    UserId = user.Id,
                    CreationDate = DateTime.UtcNow,
                    ExpiryDate = DateTime.UtcNow.AddMonths(6)
                };

                await _uow.RefreshTokenRepository.AddAsync(refreshToken);
                await _uow.ConfirmAsync();
            }

            return new AuthenticationResult
            {
                Success = true,
                Token = tokenHandler.WriteToken(token),
                RefreshToken = refreshToken.Token
            };
        }

        public async Task<AuthenticationResult> RefreshToken(string token, string refreshToken)
        {
            var validatedToken = GetPrincipalFromToken(token);

            if (validatedToken is null)
            {
                return new AuthenticationResult { Errors = new[] { "Invalid token" } };
            }

            var expiryDateUnix = long.Parse(validatedToken.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Exp).Value);
            var expiryDateTimeUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(expiryDateUnix);

            if (expiryDateTimeUtc > DateTime.UtcNow)
            {
                return new AuthenticationResult { Errors = new[] { "This token has not expired yet" } };
            }

            var jti = validatedToken.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Jti).Value;

            var storedRefreshToken = await _uow.RefreshTokenRepository.FindByToken(refreshToken);

            if (storedRefreshToken == null)
            {
                return new AuthenticationResult { Errors = new[] { "The refresh token does not exist" } };
            }

            if (DateTime.UtcNow > storedRefreshToken.ExpiryDate)
            {
                return new AuthenticationResult { Errors = new[] { "The refresh token has expired" } };
            }

            if (storedRefreshToken.Invalidated)
            {
                return new AuthenticationResult { Errors = new[] { "The refresh token has been invalidated" } };
            }

            if (storedRefreshToken.Used)
            {
                return new AuthenticationResult { Errors = new[] { "The refresh token has been used" } };
            }

            if (storedRefreshToken.JwtId != jti)
            {
                return new AuthenticationResult { Errors = new[] { "The refresh token does not exist" } };
            }

            storedRefreshToken.Used = true;
            _uow.RefreshTokenRepository.Update(storedRefreshToken);
            await _uow.ConfirmAsync();

            var user = await _uow.UserRepository.FindAsync(
                int.Parse(validatedToken.Claims.Single(x => x.Type == ClaimTypes.NameIdentifier).Value));
            var userRoles = await _uow.UserRepository.GetUserRoles(user);

            return await CreateToken(user, userRoles, storedRefreshToken);
        }

        private ClaimsPrincipal GetPrincipalFromToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            try
            {
                var principal = tokenHandler.ValidateToken(token, _tokenValidationParameters, out var validatedToken);
                
                if (!HasValidSecurityAlgorithm(validatedToken))
                {
                    return null;
                }

                return principal;
            }
            catch
            {
                return null;
            }
        }

        private bool HasValidSecurityAlgorithm(SecurityToken validatedToken)
        {
            return (validatedToken is JwtSecurityToken jwtToken)
                && jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256,
                StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
