using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Seasons.Commands;

/// <summary>Deletes a season, unless any material or season/month mapping still references it.</summary>
public sealed record DeleteSeasonCommand(int Id) : IRequest;

public sealed class DeleteSeasonHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteSeasonCommand>
{
    public async Task Handle(DeleteSeasonCommand request, CancellationToken cancellationToken)
    {
        var season = await db.Seasons.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Season), request.Id);

        var materialCount = await db.Materials.CountAsync(m => m.SeasonId == request.Id, cancellationToken);
        var monthCount = await db.SeasonMonthMaps.CountAsync(s => s.SeasonId == request.Id, cancellationToken);

        if (materialCount > 0 || monthCount > 0)
        {
            var usages = new List<string>();
            if (materialCount > 0) usages.Add($"{materialCount} material(s)");
            if (monthCount > 0) usages.Add($"{monthCount} calendar month(s)");

            throw new InvalidOperationException(
                $"Cannot delete '{season.DisplayName}' - it is used by {string.Join(" and ", usages)}. Reassign them first.");
        }

        db.Seasons.Remove(season);
        await db.SaveChangesAsync(cancellationToken);
    }
}
