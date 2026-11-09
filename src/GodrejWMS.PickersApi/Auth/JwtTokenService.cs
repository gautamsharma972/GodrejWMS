using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GodrejWMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GodrejWMS.PickersApi.Auth;

public sealed record TokenResult(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class JwtTokenService(IOptions<JwtOptions> options, UserManager<ApplicationUser> userManager)
{
    public const string SecurityStampClaim = "sstamp";

    private readonly JwtOptions _options = options.Value;

    public async Task<TokenResult> IssueTokenAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var roles = await userManager.GetRolesAsync(user);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(SecurityStampClaim, user.SecurityStamp ?? string.Empty),
            .. roles.Select(role => new Claim(ClaimTypes.Role, role)),
        ];

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
