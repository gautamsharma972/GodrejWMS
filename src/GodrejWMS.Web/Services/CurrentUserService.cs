using System.Security.Claims;
using GodrejWMS.Application.Common.Interfaces;
using Microsoft.AspNetCore.Components.Authorization;

namespace GodrejWMS.Web.Services;

/// <summary>
/// Blazor Server implementation of <see cref="ICurrentUserService"/>. Lives in the Web layer
/// (not Infrastructure) because "who is calling" is a hosting-model concern — a Razor Pages or
/// MVC host would instead read it from IHttpContextAccessor. The principal is captured once per
/// circuit/scope from <see cref="AuthenticationStateProvider"/>, which for an established circuit
/// resolves synchronously (no real I/O), so unwrapping the task here is safe.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly Lazy<ClaimsPrincipal> _user;

    public CurrentUserService(AuthenticationStateProvider authenticationStateProvider)
    {
        _user = new Lazy<ClaimsPrincipal>(() =>
            authenticationStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult().User);
    }

    public string? UserId => _user.Value.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => _user.Value.Identity?.Name;

    public bool IsInRole(string role) => _user.Value.IsInRole(role);
}
