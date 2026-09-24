using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.LocationTypes.Commands;

/// <summary>Deletes a location type, unless any location still references it.</summary>
public sealed record DeleteLocationTypeCommand(int Id) : IRequest;

public sealed class DeleteLocationTypeHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteLocationTypeCommand>
{
    public async Task Handle(DeleteLocationTypeCommand request, CancellationToken cancellationToken)
    {
        var locationType = await db.LocationTypes.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(LocationType), request.Id);

        var locationCount = await db.PalletPositions.CountAsync(p => p.LocationTypeId == request.Id, cancellationToken);

        if (locationCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{locationType.DisplayName}' - it is used by {locationCount} location(s). Reassign them first.");
        }

        db.LocationTypes.Remove(locationType);
        await db.SaveChangesAsync(cancellationToken);
    }
}
