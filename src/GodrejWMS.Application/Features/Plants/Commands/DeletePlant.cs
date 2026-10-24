using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;

namespace GodrejWMS.Application.Features.Plants.Commands;

/// <summary>Deletes a plant. Pullout lines reference a plant only by free-text name (not a
/// foreign key - see <see cref="GodrejWMS.Domain.Entities.PulloutTransaction.PlantName"/>), so
/// there is nothing else to check before removing it.</summary>
public sealed record DeletePlantCommand(int Id) : IRequest;

public sealed class DeletePlantHandler(IApplicationDbContext db)
    : IRequestHandler<DeletePlantCommand>
{
    public async Task Handle(DeletePlantCommand request, CancellationToken cancellationToken)
    {
        var plant = await db.Plants.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Plant), request.Id);

        db.Plants.Remove(plant);
        await db.SaveChangesAsync(cancellationToken);
    }
}
