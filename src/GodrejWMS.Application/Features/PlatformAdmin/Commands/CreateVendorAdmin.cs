using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.PlatformAdmin.Commands;

/// <summary>Creates another Platform Admin (vendor-only) account - a break-glass capability only
/// an existing Platform Admin can use, entirely separate from the client-facing
/// CreateUserCommand/role-assignment flow, which rejects "Platform Admin" as a target role.</summary>
public sealed record CreateVendorAdminCommand(string Email, string FullName) : IRequest<CreateVendorAdminResult>;

public sealed record CreateVendorAdminResult(string UserId, string TemporaryPassword);

public sealed class CreateVendorAdminValidator : AbstractValidator<CreateVendorAdminCommand>
{
    public CreateVendorAdminValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can create another Platform Admin account.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateVendorAdminHandler(IUserManagementService users, IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateVendorAdminCommand, CreateVendorAdminResult>
{
    public async Task<CreateVendorAdminResult> Handle(CreateVendorAdminCommand request, CancellationToken cancellationToken)
    {
        var (userId, temporaryPassword) = await users.CreateUserAsync(
            request.Email.Trim(), request.FullName.Trim(), KnownRoles.PlatformAdmin, cancellationToken);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "Created",
            EntityName = "ApplicationUser",
            EntityId = userId,
            Summary = $"Platform Admin account {request.Email} created by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);

        return new CreateVendorAdminResult(userId, temporaryPassword);
    }
}
