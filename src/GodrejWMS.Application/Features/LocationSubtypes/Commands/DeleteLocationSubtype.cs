using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.LocationSubtypes.Commands;

/// <summary>Deletes a location subtype, unless any location or stock still references it.</summary>
public sealed record DeleteLocationSubtypeCommand(int Id) : IRequest;

public sealed class DeleteLocationSubtypeHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteLocationSubtypeCommand>
{
    public async Task Handle(DeleteLocationSubtypeCommand request, CancellationToken cancellationToken)
    {
        var subtype = await db.LocationSubtypes.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(LocationSubtype), request.Id);

        var locationCount = await db.PalletPositions.CountAsync(p => p.LocationSubtypeId == request.Id, cancellationToken);
        var stockCount = await db.StockBatches.CountAsync(b => b.StockSubtypeId == request.Id, cancellationToken);

        if (locationCount > 0 || stockCount > 0)
        {
            var usages = new List<string>();
            if (locationCount > 0) usages.Add($"{locationCount} location(s)");
            if (stockCount > 0) usages.Add($"{stockCount} stock batch(es)");

            throw new InvalidOperationException(
                $"Cannot delete '{subtype.DisplayName}' - it is used by {string.Join(" and ", usages)}. Reassign them first.");
        }

        db.LocationSubtypes.Remove(subtype);
        await db.SaveChangesAsync(cancellationToken);
    }
}
