using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.Users.Commands;

/// <summary>Changes a user's role. Does not touch their warehouse/region scope assignment - a
/// Superadmin changing someone from Office Staff to Logistic Manager, say, should re-assign their
/// scope separately via AssignUserWarehouseCommand/AssignUserRegionsCommand, since the two scope
/// shapes (one warehouse vs. one or more regions) aren't interchangeable.</summary>
public sealed record SetUserRoleCommand(string UserId, string Role) : IRequest;

public sealed class SetUserRoleValidator : AbstractValidator<SetUserRoleCommand>
{
    public SetUserRoleValidator(ICurrentUserService currentUser, IRoleManagementService roles)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can assign roles.");
        RuleFor(x => x.UserId)
            .NotEmpty()
            .Must(id => id != currentUser.UserId)
            .WithMessage("You cannot change your own role.");
        RuleFor(x => x.Role)
            .MustAsync(async (r, ct) => (await roles.GetAllRoleNamesAsync(ct)).Contains(r))
            .WithMessage("Unknown role.");
    }
}

public sealed class SetUserRoleHandler(IUserManagementService users, IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<SetUserRoleCommand>
{
    public async Task Handle(SetUserRoleCommand request, CancellationToken cancellationToken)
    {
        await users.SetRoleAsync(request.UserId, request.Role, cancellationToken);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "RoleAssigned",
            EntityName = "ApplicationUser",
            EntityId = request.UserId,
            Summary = $"Role set to '{request.Role}' for user {request.UserId} by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
