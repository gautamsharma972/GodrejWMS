using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace GodrejWMS.Application.Features.Users.Commands;

public sealed record CreateUserCommand(string Email, string FullName, string Role) : IRequest<CreateUserResult>;

public sealed record CreateUserResult(string UserId, string TemporaryPassword);

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator(ICurrentUserService currentUser, IRoleManagementService roles, IUserManagementService users, IApplicationDbContext db)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can create user accounts.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role)
            .MustAsync(async (r, ct) => (await roles.GetAllRoleNamesAsync(ct)).Contains(r))
            .WithMessage("Unknown role.");
        // Platform-Admin-configured cap on provisioned (active) user accounts - null means
        // unlimited, the seeded default, so this is a no-op until a limit is explicitly set.
        RuleFor(x => x)
            .MustAsync(async (_, ct) =>
            {
                var maxUsers = await db.OrganizationSettings.Select(s => s.MaxUsers).SingleOrDefaultAsync(ct);
                if (maxUsers is null) return true;
                var activeCount = (await users.ListUsersAsync(ct)).Count(u => u.IsActive);
                return activeCount < maxUsers;
            })
            .WithMessage("The organization's user limit has been reached. Contact your administrator to increase it.");
    }
}

public sealed class CreateUserHandler(IUserManagementService users, IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateUserCommand, CreateUserResult>
{
    public async Task<CreateUserResult> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var (userId, temporaryPassword) = await users.CreateUserAsync(
            request.Email.Trim(), request.FullName.Trim(), request.Role, cancellationToken);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "Created",
            EntityName = "ApplicationUser",
            EntityId = userId,
            Summary = $"User {request.Email} created with role '{request.Role}' by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);

        return new CreateUserResult(userId, temporaryPassword);
    }
}
