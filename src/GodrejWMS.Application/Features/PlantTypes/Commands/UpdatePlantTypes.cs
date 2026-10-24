using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.PlantTypes.Commands;

public sealed record PlantTypeAssignment(int Id, string Code, string Name, int SortOrder, bool IsActive);

/// <summary>Updates the editable fields (code/name/sort/active) of existing plant types in one save.</summary>
public sealed record UpdatePlantTypesCommand(IReadOnlyList<PlantTypeAssignment> Assignments) : IRequest;

public sealed class UpdatePlantTypesValidator : AbstractValidator<UpdatePlantTypesCommand>
{
    public UpdatePlantTypesValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Id).Distinct().Count() == a.Count)
            .WithMessage("Each plant type may only appear once.");
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Count)
            .WithMessage("Plant type codes must be unique.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.Code).NotEmpty().MaximumLength(20);
            a.RuleFor(m => m.Name).NotEmpty().MaximumLength(100);
            a.RuleFor(m => m.SortOrder).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class UpdatePlantTypesHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePlantTypesCommand>
{
    public async Task Handle(UpdatePlantTypesCommand request, CancellationToken cancellationToken)
    {
        var byId = await db.PlantTypes.ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var assignment in request.Assignments)
        {
            if (!byId.TryGetValue(assignment.Id, out var row))
            {
                continue;
            }

            row.Code = assignment.Code.Trim();
            row.Name = assignment.Name.Trim();
            row.SortOrder = assignment.SortOrder;
            row.IsActive = assignment.IsActive;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedByUserId = currentUser.UserId;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
