using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.OrganizationSettings.Commands;

public sealed record UpdateOrganizationSettingsCommand(int? MaxUsers, int? MaxWarehouses) : IRequest;

public sealed class UpdateOrganizationSettingsValidator : AbstractValidator<UpdateOrganizationSettingsCommand>
{
    public UpdateOrganizationSettingsValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can configure organization settings.");
        RuleFor(x => x.MaxUsers).GreaterThan(0).When(x => x.MaxUsers is not null);
        RuleFor(x => x.MaxWarehouses).GreaterThan(0).When(x => x.MaxWarehouses is not null);
    }
}

public sealed class UpdateOrganizationSettingsHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateOrganizationSettingsCommand>
{
    public async Task Handle(UpdateOrganizationSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await db.OrganizationSettings.SingleAsync(cancellationToken);
        settings.MaxUsers = request.MaxUsers;
        settings.MaxWarehouses = request.MaxWarehouses;
        await db.SaveChangesAsync(cancellationToken);
    }
}
