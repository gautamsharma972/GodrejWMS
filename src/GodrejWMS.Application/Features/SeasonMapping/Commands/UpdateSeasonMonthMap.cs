using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.SeasonMapping.Commands;

public sealed record SeasonMonthAssignment(int Month, int SeasonId);

/// <summary>Replaces the season assigned to each of the 12 calendar months in one save.</summary>
public sealed record UpdateSeasonMonthMapCommand(IReadOnlyList<SeasonMonthAssignment> Assignments) : IRequest;

public sealed class UpdateSeasonMonthMapValidator : AbstractValidator<UpdateSeasonMonthMapCommand>
{
    public UpdateSeasonMonthMapValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Assignments).Must(a => a.Select(m => m.Month).Distinct().Count() == a.Count)
            .WithMessage("Each month may only appear once.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.Month).InclusiveBetween(1, 12);
            a.RuleFor(m => m.SeasonId)
                .MustAsync(async (id, ct) => await db.Seasons.AnyAsync(s => s.Id == id, ct))
                .WithMessage("Season is invalid.");
        });
    }
}

public sealed class UpdateSeasonMonthMapHandler(IApplicationDbContext db, IDateTimeProvider clock, ICurrentUserService currentUser)
    : IRequestHandler<UpdateSeasonMonthMapCommand>
{
    public async Task Handle(UpdateSeasonMonthMapCommand request, CancellationToken cancellationToken)
    {
        var byMonth = await db.SeasonMonthMaps.ToDictionaryAsync(s => s.Month, cancellationToken);

        foreach (var assignment in request.Assignments)
        {
            if (!byMonth.TryGetValue(assignment.Month, out var row))
            {
                continue;
            }

            row.SeasonId = assignment.SeasonId;
            row.UpdatedAt = clock.UtcNow;
            row.UpdatedByUserId = currentUser.UserId;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
