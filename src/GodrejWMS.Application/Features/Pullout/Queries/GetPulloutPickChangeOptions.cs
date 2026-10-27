using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Queries;

public sealed record GetPulloutPickChangeOptionsQuery(
    string ReferenceNumber,
    long MaterialNumber,
    string CurrentLocationCode,
    string MfgMonthLabel) : IRequest<IReadOnlyList<PulloutPickChangeOptionDto>>;

public sealed class GetPulloutPickChangeOptionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPulloutPickChangeOptionsQuery, IReadOnlyList<PulloutPickChangeOptionDto>>
{
    public async Task<IReadOnlyList<PulloutPickChangeOptionDto>> Handle(
        GetPulloutPickChangeOptionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!MfgMonthParser.TryParse(request.MfgMonthLabel, out var mfgMonth))
            throw new InvalidOperationException("The selected pick has an invalid PKM period.");

        var transaction = await db.PulloutTransactions
            .AsNoTracking()
            .Include(t => t.Lines).ThenInclude(l => l.Material)
            .Include(t => t.Lines).ThenInclude(l => l.Picks).ThenInclude(p => p.PalletPosition)
            .SingleOrDefaultAsync(t => t.ReferenceNumber == request.ReferenceNumber, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutTransaction), request.ReferenceNumber);

        if (transaction.IsConfirmed || transaction.IsRejected)
            return [];

        var line = transaction.Lines.FirstOrDefault(l =>
                l.Material.MaterialNumber == request.MaterialNumber &&
                l.Picks.Any(p => p.MfgMonth == mfgMonth
                    && p.PalletPosition.LocationCode == request.CurrentLocationCode))
            ?? throw new InvalidOperationException("The selected saved pick could not be found.");
        var matchingPicks = line.Picks
            .Where(p => p.MfgMonth == mfgMonth
                && p.PalletPosition.LocationCode == request.CurrentLocationCode)
            .ToList();
        if (matchingPicks.Count == 0)
            throw new InvalidOperationException("The selected saved pick could not be found.");

        // Multiple persisted pick rows can legitimately represent the same logical source after
        // successive reallocations. Treat them as one source instead of requiring uniqueness.
        var pick = matchingPicks[0];
        var matchingPickIds = matchingPicks.Select(p => p.Id).ToHashSet();
        var materialId = line.MaterialId;
        var otherQuantities = transaction.Lines
            .SelectMany(l => l.Picks)
            .Where(p => !matchingPickIds.Contains(p.Id) && p.PulloutTransactionLine.MaterialId == materialId)
            .GroupBy(p => (p.PalletPositionId, p.MfgMonth))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.QuantityBoxes));

        var batches = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.MaterialId == materialId
                && b.StockSubtypeId == LocationSubtypeIds.Good
                && b.QuantityBoxes > 0
                && b.PalletPosition.IsActive
                && b.PalletPosition.LocationSubtypeId == LocationSubtypeIds.Good)
            .Select(b => new
            {
                b.PalletPositionId,
                b.PalletPosition.LocationCode,
                b.MfgMonth,
                b.QuantityBoxes,
                b.PalletPosition.Rack.Code,
                b.PalletPosition.Column,
                b.PalletPosition.Level
            })
            .ToListAsync(cancellationToken);

        return batches
            .Select(b => new PulloutPickChangeOptionDto(
                b.PalletPositionId,
                b.LocationCode,
                MfgMonthParser.Format(b.MfgMonth),
                b.QuantityBoxes,
                Math.Max(0, b.QuantityBoxes - otherQuantities.GetValueOrDefault((b.PalletPositionId, b.MfgMonth))),
                b.PalletPositionId == pick.PalletPositionId && b.MfgMonth == pick.MfgMonth))
            .Where(o => o.AvailableBoxes > 0)
            .OrderByDescending(o => o.IsCurrent)
            .ThenBy(o => o.LocationCode)
            .ToList();
    }
}
