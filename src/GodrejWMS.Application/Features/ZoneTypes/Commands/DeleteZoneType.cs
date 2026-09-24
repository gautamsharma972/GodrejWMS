using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.ZoneTypes.Commands;

/// <summary>Deletes a zone type, unless any location still references it.</summary>
public sealed record DeleteZoneTypeCommand(int Id) : IRequest;

public sealed class DeleteZoneTypeHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteZoneTypeCommand>
{
    public async Task Handle(DeleteZoneTypeCommand request, CancellationToken cancellationToken)
    {
        var zoneType = await db.ZoneTypes.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(ZoneType), request.Id);

        var locationCount = await db.PalletPositions.CountAsync(p => p.ZoneTypeId == request.Id, cancellationToken);

        if (locationCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{zoneType.DisplayName}' - it is used by {locationCount} location(s). Reassign them first.");
        }

        db.ZoneTypes.Remove(zoneType);
        await db.SaveChangesAsync(cancellationToken);
    }
}
