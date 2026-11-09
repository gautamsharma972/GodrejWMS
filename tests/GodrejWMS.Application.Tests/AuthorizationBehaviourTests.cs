using System.Reflection;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Behaviours;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using MediatR;
using Xunit;

namespace GodrejWMS.Application.Tests;

/// <summary>Server-side authorization: a request marked [RequiresPermission]/[RequiresRole] is refused unless the user holds it,
/// regardless of which screen or API endpoint sent it.</summary>
public class AuthorizationBehaviourTests
{
    [RequiresPermission("A.One", "A.Two")]
    private sealed record AnyOfRequest : IRequest<string>;

    [RequiresPermission("A.One")]
    [RequiresPermission("B.Two")]
    private sealed record AllOfRequest : IRequest<string>;

    [RequiresRole(KnownRoles.PlatformAdmin)]
    private sealed record RoleRequest : IRequest<string>;

    private sealed record OpenRequest : IRequest<string>;

    private sealed class FakePermissions(params string[] granted) : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(granted.Contains(permissionKey));
    }

    private sealed class FakeUser(string? userId, params string[] roles) : ICurrentUserService
    {
        public string? UserId { get; } = userId;
        public string? UserName => "tester";
        public IReadOnlyList<string> Roles { get; } = roles;
        public bool IsInRole(string role) => roles.Contains(role);
    }

    private static Task<string> RunAsync<TRequest>(TRequest request, IPermissionService permissions, ICurrentUserService user)
        where TRequest : IRequest<string> =>
        new AuthorizationBehaviour<TRequest, string>(permissions, user).Handle(request, _ => Task.FromResult("ok"), default);

    [Fact]
    public async Task AnyOfPermissions_AllowsAUserHoldingEither() =>
        Assert.Equal("ok", await RunAsync(new AnyOfRequest(), new FakePermissions("A.Two"), new FakeUser("u1", "Supervisor")));

    [Fact]
    public async Task MissingPermission_IsRefused() =>
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => RunAsync(new AnyOfRequest(), new FakePermissions("Other"), new FakeUser("u1", "Supervisor")));

    [Fact]
    public async Task SeveralAttributes_AllMustBeSatisfied()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => RunAsync(new AllOfRequest(), new FakePermissions("A.One"), new FakeUser("u1")));
        Assert.Equal("ok", await RunAsync(new AllOfRequest(), new FakePermissions("A.One", "B.Two"), new FakeUser("u1")));
    }

    [Fact]
    public async Task AnonymousUser_IsRefused() =>
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => RunAsync(new AnyOfRequest(), new FakePermissions("A.One"), new FakeUser(null)));

    [Fact]
    public async Task PlatformAdmin_PassesPermissionChecks() =>
        Assert.Equal("ok", await RunAsync(new AnyOfRequest(), new FakePermissions(), new FakeUser("u1", KnownRoles.PlatformAdmin)));

    [Fact]
    public async Task RoleRule_NeedsTheRole_AndSuperadminAloneIsNotEnough()
    {
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => RunAsync(new RoleRequest(), new FakePermissions(), new FakeUser("u1", KnownRoles.Superadmin)));
        Assert.Equal("ok", await RunAsync(new RoleRequest(), new FakePermissions(), new FakeUser("u1", KnownRoles.PlatformAdmin)));
    }

    [Fact]
    public async Task UnmarkedRequests_AreNotChecked() =>
        Assert.Equal("ok", await RunAsync(new OpenRequest(), new FakePermissions(), new FakeUser(null)));

    /// <summary>Guard against a new command shipping with no server-side check. Everything not marked must be in this list,
    /// i.e. it enforces access itself (in its handler via IPermissionService, or in its validator for the Superadmin-only account commands).</summary>
    [Fact]
    public void EveryCommand_IsMarked_OrEnforcesAccessItself()
    {
        string[] selfEnforcing =
        [
            // in the handler, through IPermissionService
            "ApproveInventoryAdjustmentCommand", "RejectInventoryAdjustmentCommand", "CancelInventoryAdjustmentCommand", "SubmitInventoryAdjustmentCommand",
            "AssignInwardPutawayPkmCommand", "ConfirmInwardPutawayCommand", "ConfirmPickerPutawayCommand", "ConfirmSingleInwardPutawayCommand",
            "PickerPlaceInwardPutawayCommand", "ConfirmPulloutCommand", "ConfirmPulloutPickCommand", "ConfirmPulloutPicklistCommand",
            "ImportStockMasterFromExcelCommand", "DeleteUserCommand", "UpdateUserCommand",
            // in the validator (platform-level only, plus the role-hierarchy rules)
            "AssignUserRegionsCommand", "AssignUserWarehouseCommand", "CreateUserCommand", "SetUserActiveCommand", "SetUserRoleCommand"
        ];

        var unmarked = typeof(GodrejWMS.Application.DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Command", StringComparison.Ordinal)
                && t.GetInterfaces().Any(i => i == typeof(IBaseRequest)))
            .Where(t => t.GetCustomAttributes<RequiresPermissionAttribute>(true).Any() is false && t.GetCustomAttributes<RequiresRoleAttribute>(true).Any() is false)
            .Select(t => t.Name)
            .Except(selfEnforcing)
            .OrderBy(n => n)
            .ToList();

        Assert.True(unmarked.Count == 0, "Commands with no server-side access check: " + string.Join(", ", unmarked));
    }
}
