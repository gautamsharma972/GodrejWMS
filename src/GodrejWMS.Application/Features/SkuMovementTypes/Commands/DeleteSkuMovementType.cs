using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.SkuMovementTypes.Commands;

/// <summary>Deletes a SKU movement type, unless any material still references it.</summary>
public sealed record DeleteSkuMovementTypeCommand(int Id) : IRequest;

public sealed class DeleteSkuMovementTypeHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteSkuMovementTypeCommand>
{
    public async Task Handle(DeleteSkuMovementTypeCommand request, CancellationToken cancellationToken)
    {
        var movementType = await db.SkuMovementTypes.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(SkuMovementType), request.Id);

        var materialCount = await db.Materials.CountAsync(m => m.MovementTypeId == request.Id, cancellationToken);

        if (materialCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{movementType.DisplayName}' - it is used by {materialCount} material(s). Reassign them first.");
        }

        db.SkuMovementTypes.Remove(movementType);
        await db.SaveChangesAsync(cancellationToken);
    }
}
