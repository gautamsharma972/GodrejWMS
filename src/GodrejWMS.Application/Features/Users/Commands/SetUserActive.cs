using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.Users.Commands;

public sealed record SetUserActiveCommand(string UserId, bool IsActive) : IRequest;

public sealed class SetUserActiveValidator : AbstractValidator<SetUserActiveCommand>
{
    public SetUserActiveValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can activate or deactivate accounts.");
        RuleFor(x => x.UserId)
            .NotEmpty()
            .Must(id => id != currentUser.UserId)
            .WithMessage("You cannot deactivate your own account.");
    }
}

public sealed class SetUserActiveHandler(IUserManagementService users, IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<SetUserActiveCommand>
{
    public async Task Handle(SetUserActiveCommand request, CancellationToken cancellationToken)
    {
        await users.SetActiveAsync(request.UserId, request.IsActive, cancellationToken);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = request.IsActive ? "Activated" : "Deactivated",
            EntityName = "ApplicationUser",
            EntityId = request.UserId,
            Summary = $"User {request.UserId} {(request.IsActive ? "activated" : "deactivated")} by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
