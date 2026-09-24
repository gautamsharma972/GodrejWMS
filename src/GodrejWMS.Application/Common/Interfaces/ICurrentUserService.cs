namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>Exposes the identity of the caller to the Application layer without depending on ASP.NET Core.</summary>
public interface ICurrentUserService
{
    string? UserId { get; }

    string? UserName { get; }

    bool IsInRole(string role);
}
