using System.Collections.Concurrent;
using System.Reflection;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using MediatR;

namespace GodrejWMS.Application.Common.Behaviours;

/// <summary>Runs before validation and the handler: rejects a request whose [RequiresPermission] / [RequiresRole] the current user
/// does not satisfy, so authorization no longer depends on a page hiding a button.</summary>
public sealed class AuthorizationBehaviour<TRequest, TResponse>(IPermissionService permissions, ICurrentUserService currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Type, (RequiresPermissionAttribute[] Permissions, RequiresRoleAttribute[] Roles)> Cache = new();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var (permissionRules, roleRules) = Cache.GetOrAdd(typeof(TRequest), type =>
            (type.GetCustomAttributes<RequiresPermissionAttribute>(true).ToArray(), type.GetCustomAttributes<RequiresRoleAttribute>(true).ToArray()));

        if (permissionRules.Length == 0 && roleRules.Length == 0)
        {
            return await next(cancellationToken);
        }

        if (string.IsNullOrEmpty(currentUser.UserId))
        {
            throw new ForbiddenAccessException("You must be signed in to do this.");
        }

        foreach (var rule in roleRules)
        {
            if (!rule.AnyOf.Any(currentUser.IsInRole))
            {
                throw new ForbiddenAccessException("You do not have the role required for this action.");
            }
        }

        if (permissionRules.Length > 0 && !currentUser.IsInRole(KnownRoles.PlatformAdmin))
        {
            foreach (var rule in permissionRules)
            {
                var allowed = false;
                foreach (var key in rule.AnyOf)
                {
                    if (await permissions.HasPermissionAsync(key, cancellationToken))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new ForbiddenAccessException("You do not have permission to do this.");
                }
            }
        }

        return await next(cancellationToken);
    }
}
