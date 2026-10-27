using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Users.Commands;

namespace GodrejWMS.Application.Tests;

public class UserAccountCommandValidatorTests
{
    [Fact]
    public async Task UpdateUser_AllowsGrantedEditorForStandardAccount()
    {
        var validator = new UpdateUserValidator(
            new PermissionStub(true),
            new UserServiceStub(new UserAccountDto("target", "old@example.com", "Old Name", true, [KnownRoles.OfficeStaff])));

        var result = await validator.ValidateAsync(new UpdateUserCommand("target", "new@example.com", "New Name"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task DeleteUser_RejectsDeletingOwnAccount()
    {
        var validator = new DeleteUserValidator(
            new PermissionStub(true),
            new UserServiceStub(new UserAccountDto("caller", "me@example.com", "Me", true, [KnownRoles.Superadmin])),
            new CurrentUserStub("caller"));

        var result = await validator.ValidateAsync(new DeleteUserCommand("caller"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("own account"));
    }

    [Fact]
    public async Task DeleteUser_RejectsPlatformAdminTarget()
    {
        var validator = new DeleteUserValidator(
            new PermissionStub(true),
            new UserServiceStub(new UserAccountDto("platform", "platform@example.com", "Platform", true, [KnownRoles.PlatformAdmin])),
            new CurrentUserStub("caller"));

        var result = await validator.ValidateAsync(new DeleteUserCommand("platform"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Platform Admin"));
    }

    private sealed class PermissionStub(bool granted) : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(granted);
    }

    private sealed class CurrentUserStub(string userId) : ICurrentUserService
    {
        public string? UserId => userId;
        public string? UserName => "caller@example.com";
        public bool IsInRole(string role) => role == KnownRoles.Superadmin;
        public IReadOnlyList<string> Roles => [KnownRoles.Superadmin];
    }

    private sealed class UserServiceStub(UserAccountDto? account) : IUserManagementService
    {
        public Task<IReadOnlyList<UserAccountDto>> ListUsersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserAccountDto>>(account is null ? [] : [account]);

        public Task<IReadOnlyList<UserAccountDto>> ListAllUsersAsync(CancellationToken cancellationToken) =>
            ListUsersAsync(cancellationToken);

        public Task<UserAccountDto?> GetUserAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(account?.UserId == userId ? account : null);

        public Task<(string UserId, string TemporaryPassword)> CreateUserAsync(
            string email, string fullName, string role, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(string userId, bool isActive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateUserAsync(string userId, string email, string fullName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetRoleAsync(string userId, string role, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> ResetPasswordAsync(string userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
