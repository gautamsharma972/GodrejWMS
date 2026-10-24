using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.PlantTypes.Commands;

/// <summary>Deletes a plant type, unless any plant still references it.</summary>
public sealed record DeletePlantTypeCommand(int Id) : IRequest;

public sealed class DeletePlantTypeHandler(IApplicationDbContext db)
    : IRequestHandler<DeletePlantTypeCommand>
{
    public async Task Handle(DeletePlantTypeCommand request, CancellationToken cancellationToken)
    {
        var plantType = await db.PlantTypes.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(PlantType), request.Id);

        var plantCount = await db.Plants.CountAsync(p => p.PlantTypeId == request.Id, cancellationToken);

        if (plantCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{plantType.Name}' - it is used by {plantCount} plant(s). Reassign them first.");
        }

        db.PlantTypes.Remove(plantType);
        await db.SaveChangesAsync(cancellationToken);
    }
}
