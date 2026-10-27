using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Cities.Commands;

/// <summary>Deletes a city, unless any warehouse still references it.</summary>
public sealed record DeleteCityCommand(int Id) : IRequest;

public sealed class DeleteCityHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteCityCommand>
{
    public async Task Handle(DeleteCityCommand request, CancellationToken cancellationToken)
    {
        var city = await db.Cities.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(City), request.Id);

        var warehouseCount = await db.Warehouses.CountAsync(w => w.CityId == request.Id, cancellationToken);
        if (warehouseCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{city.Name}' - it is used by {warehouseCount} warehouse(s). Deactivate it instead.");
        }

        db.Cities.Remove(city);
        await db.SaveChangesAsync(cancellationToken);
    }
}
