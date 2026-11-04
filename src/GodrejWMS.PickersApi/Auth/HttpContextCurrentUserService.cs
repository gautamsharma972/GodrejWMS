using System.Security.Claims;
using GodrejWMS.Application.Common.Interfaces;

namespace GodrejWMS.PickersApi.Auth;

/// <summary>API-host implementation of <see cref="ICurrentUserService"/>, reading the principal
/// populated by JWT bearer authentication off <see cref="IHttpContextAccessor"/>. "Who is calling"
/// is a hosting-model concern (see GodrejWMS.Web.Services.CurrentUserService's own doc comment,
/// which names IHttpContextAccessor as exactly the non-Blazor equivalent) - this is that sibling
/// implementation for the Pickers API host.</summary>
public sealed class HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => User?.Identity?.Name;

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;

    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];
}
