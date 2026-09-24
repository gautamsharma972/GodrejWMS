using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Seasons.Commands;

public sealed record SeasonAssignment(int Id, string DisplayName, string? Description, int SortOrder, bool IsActive);

/// <summary>
/// Updates the editable fields (name/description/sort/active) of the fixed set of seasons in one
/// save. The Id/Code pairing is fixed - seasons can't be added or removed here.
/// </summary>
public sealed record UpdateSeasonsCommand(IReadOnlyList<SeasonAssignment> Assignments) : IRequest;

public sealed class UpdateSeasonsValidator : AbstractValidator<UpdateSeasonsCommand>
{
    public UpdateSeasonsValidator()
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Id).Distinct().Count() == a.Count)
            .WithMessage("Each season may only appear once.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.DisplayName).NotEmpty().MaximumLength(100);
            a.RuleFor(m => m.Description).MaximumLength(300);
            a.RuleFor(m => m.SortOrder).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class UpdateSeasonsHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateSeasonsCommand>
{
    public async Task Handle(UpdateSeasonsCommand request, CancellationToken cancellationToken)
    {
        var byId = await db.Seasons.ToDictionaryAsync(s => s.Id, cancellationToken);

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
