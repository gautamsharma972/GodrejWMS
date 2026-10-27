using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.UserScope.Commands;

/// <summary>Sets the full list of regions a Logistic Manager can see (every warehouse within
/// each). Replaces any prior assignment rather than adding to it.</summary>
public sealed record AssignUserRegionsCommand(string UserId, IReadOnlyList<int> RegionIds) : IRequest;

public sealed class AssignUserRegionsValidator : AbstractValidator<AssignUserRegionsCommand>
{
    public AssignUserRegionsValidator(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can assign region scope.");
        RuleFor(x => x.UserId)
            .NotEmpty()
            .Must(id => id != currentUser.UserId)
            .WithMessage("You cannot change your own scope assignment.");
        RuleFor(x => x.RegionIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Each region may only be assigned once.");
        RuleForEach(x => x.RegionIds)
            .MustAsync(async (id, ct) => await db.Regions.AnyAsync(r => r.Id == id, ct))
            .WithMessage("One or more regions are invalid.");
    }
}

public sealed class AssignUserRegionsHandler(IApplicationDbContext db)
    : IRequestHandler<AssignUserRegionsCommand>
{
    public async Task Handle(AssignUserRegionsCommand request, CancellationToken cancellationToken)
    {
        var existing = await db.UserRegions.Where(r => r.UserId == request.UserId).ToListAsync(cancellationToken);
        db.UserRegions.RemoveRange(existing);

        db.UserRegions.AddRange(request.RegionIds.Select(regionId =>
            new UserRegion { UserId = request.UserId, RegionId = regionId }));

        await db.SaveChangesAsync(cancellationToken);
    }
}
