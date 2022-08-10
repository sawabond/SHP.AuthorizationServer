using DAL.Entities;
using System.Collections.Generic;

namespace IdentityServer.Services.Interfaces
{
    public interface ITokenService
    {
        string CreateToken(AppUser user, ICollection<string> roles);

        string RefreshToken(string token, string refreshToken);
    }
}
