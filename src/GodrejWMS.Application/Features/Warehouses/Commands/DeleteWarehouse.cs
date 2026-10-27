using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Warehouses.Commands;

/// <summary>Deletes a warehouse, unless it has racks (and therefore locations/pallets/inventory
/// transitively) or inward/pullout/adjustment transaction history - per spec 2.5, deactivate
/// instead in that case.</summary>
public sealed record DeleteWarehouseCommand(int Id) : IRequest;

public sealed class DeleteWarehouseHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteWarehouseCommand>
{
    public async Task Handle(DeleteWarehouseCommand request, CancellationToken cancellationToken)
    {
        var warehouse = await db.Warehouses.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Warehouse), request.Id);

        var rackCount = await db.Racks.CountAsync(r => r.WarehouseId == request.Id, cancellationToken);
        var inwardCount = await db.InwardTransactions.CountAsync(t => t.WarehouseId == request.Id, cancellationToken);
        var pulloutCount = await db.PulloutTransactions.CountAsync(t => t.WarehouseId == request.Id, cancellationToken);
        var adjustmentCount = await db.InventoryAdjustments.CountAsync(a => a.WarehouseId == request.Id, cancellationToken);

        if (rackCount > 0 || inwardCount > 0 || pulloutCount > 0 || adjustmentCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{warehouse.Name}' - it has racks/locations and/or transaction history. Deactivate it instead.");
        }

        db.Warehouses.Remove(warehouse);
        await db.SaveChangesAsync(cancellationToken);
    }
}
