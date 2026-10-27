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
            else if (target.Roles.Contains(KnownRoles.PlatformAdmin))
            {
                context.AddFailure(nameof(command.UserId), "Platform Admin accounts cannot be deleted here.");
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
        await db.SaveChangesAsync(cancellationToken);

        await users.DeleteUserAsync(request.UserId, cancellationToken);

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
        await db.SaveChangesAsync(cancellationToken);
    }
}
