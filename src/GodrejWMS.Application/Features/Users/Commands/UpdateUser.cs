using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using MediatR;

namespace GodrejWMS.Application.Features.Users.Commands;

public sealed record UpdateUserCommand(string UserId, string Email, string FullName) : IRequest;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator(IPermissionService permissions, IUserManagementService users, ICurrentUserService currentUser)
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x).CustomAsync(async (command, context, ct) =>
        {
            if (!await permissions.HasPermissionAsync(PermissionKeys.Users.Edit, ct))
            {
                context.AddFailure("You don't have permission to edit user accounts.");
                return;
            }

            var target = await users.GetUserAsync(command.UserId, ct);
            if (target is null)
            {
                context.AddFailure(nameof(command.UserId), "User not found.");
            }
            else if (command.UserId != currentUser.UserId
                && UserRoleHierarchy.ValidateTarget(currentUser, target.Roles) is { } hierarchyError)
            {
                context.AddFailure(nameof(command.UserId), hierarchyError);
            }
        });
    }
}

public sealed class UpdateUserHandler(
    IUserManagementService users,
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var fullName = request.FullName.Trim();
        await users.UpdateUserAsync(request.UserId, email, fullName, cancellationToken);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "Updated",
            EntityName = "ApplicationUser",
            EntityId = request.UserId,
            Summary = $"User {email} account details updated by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
