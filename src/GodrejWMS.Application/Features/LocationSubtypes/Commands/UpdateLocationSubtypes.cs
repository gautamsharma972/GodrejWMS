using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.LocationSubtypes.Commands;

public sealed record LocationSubtypeAssignment(int Id, string DisplayName, string? Description, int SortOrder, bool IsActive);

/// <summary>
/// Updates the editable fields (name/description/sort/active) of the fixed set of location subtypes
/// in one save. The Id/Code pairing is fixed - subtypes can't be added or removed here.
/// </summary>
public sealed record UpdateLocationSubtypesCommand(IReadOnlyList<LocationSubtypeAssignment> Assignments) : IRequest;

public sealed class UpdateLocationSubtypesValidator : AbstractValidator<UpdateLocationSubtypesCommand>
{
    public UpdateLocationSubtypesValidator()
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Id).Distinct().Count() == a.Count)
            .WithMessage("Each location subtype may only appear once.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.DisplayName).NotEmpty().MaximumLength(100);
            a.RuleFor(m => m.Description).MaximumLength(300);
            a.RuleFor(m => m.SortOrder).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class UpdateLocationSubtypesHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateLocationSubtypesCommand>
{
    public async Task Handle(UpdateLocationSubtypesCommand request, CancellationToken cancellationToken)
    {
        var byId = await db.LocationSubtypes.ToDictionaryAsync(s => s.Id, cancellationToken);

        foreach (var assignment in request.Assignments)
        {
            if (!byId.TryGetValue(assignment.Id, out var row))
            {
                continue;
            }

            row.DisplayName = assignment.DisplayName.Trim();
            row.Description = string.IsNullOrWhiteSpace(assignment.Description) ? null : assignment.Description.Trim();
            row.SortOrder = assignment.SortOrder;
            row.IsActive = assignment.IsActive;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedByUserId = currentUser.UserId;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
