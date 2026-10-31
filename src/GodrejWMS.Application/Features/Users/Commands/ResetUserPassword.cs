using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Users.Commands;

/// <summary>Resets a user's password to a new, randomly generated one, per spec section 14.
/// Superadmin can reset anyone. Head Supervisor can reset only users assigned to their own
/// warehouse, and never a Superadmin or Logistic Manager target (section 14.2) - validated here,
/// server-side, on every call, independent of what the UI shows (section 14.3: "validate the
/// target user's warehouse assignment on the backend every time a reset is requested").</summary>
public sealed record ResetUserPasswordCommand(string UserId) : IRequest<string>;

public sealed class ResetUserPasswordValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordValidator(ICurrentUserService currentUser, IApplicationDbContext db, IUserManagementService users)
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .Must(id => id != currentUser.UserId)
            .WithMessage("Use your own account's change-password option instead of resetting it here.");

        RuleFor(x => x).CustomAsync(async (command, context, ct) =>
        {
            var target = await users.GetUserAsync(command.UserId, ct);
            if (target is null)
            {
                context.AddFailure("UserId", "User not found.");
                return;
            }

            var hierarchyError = UserRoleHierarchy.ValidateTarget(currentUser, target.Roles);
            if (hierarchyError is not null)
            {
                context.AddFailure("UserId", hierarchyError);
                return;
            }

            if (currentUser.IsPlatformLevel())
            {
                return;
            }

            if (!currentUser.IsInRole(KnownRoles.HeadSupervisor))
            {
                context.AddFailure("UserId", "You don't have permission to reset other users' passwords.");
                return;
            }

            if (target.Roles.Any(role => role is KnownRoles.Superadmin or KnownRoles.LogisticManager))
            {
                context.AddFailure("UserId", "Head Supervisor cannot reset a Superadmin or Logistic Manager password.");
                return;
            }

            var callerWarehouseId = await db.UserWarehouses
                .Where(w => w.UserId == currentUser.UserId)
                .Select(w => (int?)w.WarehouseId)
                .FirstOrDefaultAsync(ct);
            var targetWarehouseId = await db.UserWarehouses
                .Where(w => w.UserId == command.UserId)
                .Select(w => (int?)w.WarehouseId)
                .FirstOrDefaultAsync(ct);

            if (callerWarehouseId is null || targetWarehouseId is null || callerWarehouseId != targetWarehouseId)
            {
                context.AddFailure("UserId", "Head Supervisor can only reset passwords for users assigned to their own warehouse.");
            }
        });
    }
}

public sealed class ResetUserPasswordHandler(IUserManagementService users, IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<ResetUserPasswordCommand, string>
{
    public async Task<string> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var temporaryPassword = await users.ResetPasswordAsync(request.UserId, cancellationToken);

        // The generic change-tracking audit sink explicitly skips ApplicationUser (so a plain
        // "Updated" entry never risks capturing a password hash) - this is the one place that
        // records the reset event itself, and it only ever records who/what/when, never the new
        // password (spec 14.4: "Never record the password, temporary password, reset token... in
        // audit logs").
        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "PasswordReset",
            EntityName = "ApplicationUser",
            EntityId = request.UserId,
            Summary = $"Password reset for user {request.UserId} by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);

        return temporaryPassword;
    }
}
