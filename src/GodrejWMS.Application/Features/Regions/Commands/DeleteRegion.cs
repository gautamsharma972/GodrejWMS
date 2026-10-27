using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Regions.Commands;

/// <summary>Deletes a region, unless it has dependent states (per spec 2.2: "prevent deletion of
/// regions containing dependent states, cities, warehouses, or operational records").</summary>
public sealed record DeleteRegionCommand(int Id) : IRequest;

public sealed class DeleteRegionHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteRegionCommand>
{
    public async Task Handle(DeleteRegionCommand request, CancellationToken cancellationToken)
    {
        var region = await db.Regions.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Region), request.Id);

        var stateCount = await db.States.CountAsync(s => s.RegionId == request.Id, cancellationToken);
        if (stateCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{region.Name}' - it has {stateCount} state(s) under it. Deactivate it instead.");
        }

        db.Regions.Remove(region);
        await db.SaveChangesAsync(cancellationToken);
    }
}
