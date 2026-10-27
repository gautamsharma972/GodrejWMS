using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.WarehouseCategories.Commands;

/// <summary>Deletes a warehouse category, unless any warehouse still references it (per spec 2.1: "prevent deletion of categories that are already referenced by warehouses").</summary>
public sealed record DeleteWarehouseCategoryCommand(int Id) : IRequest;

public sealed class DeleteWarehouseCategoryHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteWarehouseCategoryCommand>
{
    public async Task Handle(DeleteWarehouseCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await db.WarehouseCategories.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(WarehouseCategory), request.Id);

        var warehouseCount = await db.Warehouses.CountAsync(w => w.WarehouseCategoryId == request.Id, cancellationToken);
        if (warehouseCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{category.Name}' - it is used by {warehouseCount} warehouse(s). Deactivate it instead, or reassign them first.");
        }

        db.WarehouseCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }
}
