using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.Roles.Commands;

public sealed record DeleteRoleCommand(string Name) : IRequest;

public sealed class DeleteRoleValidator : AbstractValidator<DeleteRoleCommand>
{
    public DeleteRoleValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can delete roles.");
        RuleFor(x => x.Name).NotEmpty();
    }
}

public sealed class DeleteRoleHandler(IRoleManagementService roles, IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<DeleteRoleCommand>
{
    public async Task Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await roles.DeleteRoleAsync(name, cancellationToken);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "Deleted",
            EntityName = "Role",
            EntityId = name,
            Summary = $"Role '{name}' deleted by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
