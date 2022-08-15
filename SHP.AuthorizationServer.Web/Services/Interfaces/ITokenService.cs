using DAL.Entities;
using SHP.AuthorizationServer.Web.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace IdentityServer.Services.Interfaces
{
    public interface ITokenService
    {
        Task<AuthenticationResult> CreateToken(AppUser user, ICollection<string> roles, RefreshToken refreshToken=null);

        Task<AuthenticationResult> RefreshToken(string token, string refreshToken);

        Task<AuthenticationResult> RevokeToken(string refreshToken);
    }
}
