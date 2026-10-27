using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.Roles.Commands;

public sealed record CreateRoleCommand(string Name) : IRequest;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can create roles.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(50);
    }
}

public sealed class CreateRoleHandler(IRoleManagementService roles, IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<CreateRoleCommand>
{
    public async Task Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await roles.CreateRoleAsync(name, cancellationToken);

        db.ActivityLogs.Add(new Domain.Entities.ActivityLog
        {
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            Action = "Created",
            EntityName = "Role",
            EntityId = name,
            Summary = $"Role '{name}' created by {currentUser.UserName}."
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
