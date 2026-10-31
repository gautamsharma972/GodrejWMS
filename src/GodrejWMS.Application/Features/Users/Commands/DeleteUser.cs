using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Users.Commands;

public sealed record DeleteUserCommand(string UserId) : IRequest;

public sealed class DeleteUserValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserValidator(
        IPermissionService permissions,
        IUserManagementService users,
        ICurrentUserService currentUser)
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .Must(id => id != currentUser.UserId)
            .WithMessage("You cannot delete your own account.");
        RuleFor(x => x).CustomAsync(async (command, context, ct) =>
        {
            if (!await permissions.HasPermissionAsync(PermissionKeys.Users.Delete, ct))
            {
                context.AddFailure("You don't have permission to delete user accounts.");
                return;
            }

            var target = await users.GetUserAsync(command.UserId, ct);
            if (target is null)
            {
                context.AddFailure(nameof(command.UserId), "User not found.");
            }
            else if (UserRoleHierarchy.ValidateTarget(currentUser, target.Roles) is { } hierarchyError)
            {
                context.AddFailure(nameof(command.UserId), hierarchyError);
            }
        });
    }
}

public sealed class DeleteUserHandler(
    IUserManagementService users,
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<DeleteUserCommand>
{
    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var target = await users.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException("User not found.");

        var warehouseScopes = await db.UserWarehouses
            .Where(x => x.UserId == request.UserId)
            .ToListAsync(cancellationToken);
        var regionScopes = await db.UserRegions
            .Where(x => x.UserId == request.UserId)
            .ToListAsync(cancellationToken);
        db.UserWarehouses.RemoveRange(warehouseScopes);
        db.UserRegions.RemoveRange(regionScopes);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "Deleted",
            EntityName = "ApplicationUser",
            EntityId = request.UserId,
            Summary = $"User {target.Email} deleted by {currentUser.UserName}."
        });

        // UserManager and IApplicationDbContext resolve to the same scoped AppDbContext. Its
        // DeleteAsync save therefore commits the Identity deletion, scope removals and audit row
        // together instead of leaving a half-deleted account if a second save fails.
        await users.DeleteUserAsync(request.UserId, cancellationToken);
    }
}
