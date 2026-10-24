using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Plants.Commands;

public sealed record PlantAssignment(int Id, string Code, string Name, int? PlantTypeId, string? Description, bool IsActive);

/// <summary>Updates the editable fields of existing plants in one save.</summary>
public sealed record UpdatePlantsCommand(IReadOnlyList<PlantAssignment> Assignments) : IRequest;

public sealed class UpdatePlantsValidator : AbstractValidator<UpdatePlantsCommand>
{
    public UpdatePlantsValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Id).Distinct().Count() == a.Count)
            .WithMessage("Each plant may only appear once.");
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == a.Count)
            .WithMessage("Plant codes must be unique.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.Code).NotEmpty().MaximumLength(20);
            a.RuleFor(m => m.Name).NotEmpty().MaximumLength(100);
            a.RuleFor(m => m.Description).MaximumLength(300);
        });
    }
}

public sealed class UpdatePlantsHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdatePlantsCommand>
{
    public async Task Handle(UpdatePlantsCommand request, CancellationToken cancellationToken)
    {
        var byId = await db.Plants.ToDictionaryAsync(p => p.Id, cancellationToken);
        var validPlantTypeIds = await db.PlantTypes.Select(t => t.Id).ToListAsync(cancellationToken);

        foreach (var assignment in request.Assignments)
        {
            if (!byId.TryGetValue(assignment.Id, out var row))
            {
                continue;
            }

            row.Code = assignment.Code.Trim();
            row.Name = assignment.Name.Trim();
            row.PlantTypeId = assignment.PlantTypeId is { } typeId && validPlantTypeIds.Contains(typeId) ? typeId : null;
            row.Description = string.IsNullOrWhiteSpace(assignment.Description) ? null : assignment.Description.Trim();
            row.IsActive = assignment.IsActive;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedByUserId = currentUser.UserId;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
