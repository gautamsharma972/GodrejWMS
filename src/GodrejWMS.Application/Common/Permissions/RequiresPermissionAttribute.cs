namespace GodrejWMS.Application.Common.Permissions;

/// <summary>Marks a MediatR request as callable only by a user holding at least one of the listed permissions (Role &amp; Permission
/// matrix). Several attributes on one request must all be satisfied. Enforced server-side by AuthorizationBehaviour, so it holds
/// whichever screen or API endpoint sends the request. A Platform Admin (vendor-level role) passes.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresPermissionAttribute(params string[] anyOf) : Attribute
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}

/// <summary>Marks a request as callable only by a user in at least one of the listed roles.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresRoleAttribute(params string[] anyOf) : Attribute
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}
