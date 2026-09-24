using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward;

/// <summary>
/// Shared confirmation logic for a single put-away, used by both <c>ConfirmInwardPutaway</c>
/// (confirms every pending line in a transaction at once) and <c>ConfirmSingleInwardPutaway</c>
/// (confirms one recommended location at a time, for the mobile Putaway Execution screen).
/// Re-validates against the CURRENT database state, not the assumptions the recommendation was
/// made under, since another user's confirmation, reroute, or a location edit may have changed
/// things since this put-away was reserved.
/// </summary>
internal static class InwardPutawayConfirmationLogic
{
    public static async Task ConfirmAsync(
        IApplicationDbContext db,
        InwardTransactionLine line,
        InwardPutaway putaway,
        IDateTimeProvider clock,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        var position = putaway.PalletPosition;

        if (!position.IsActive)
        {
            throw new InvalidOperationException(
                $"Location {position.LocationCode} is no longer active and cannot receive this put-away. Re-route it to a new location.");
        }

        if (line.Material.RequirePreferredZone && line.Material.PreferredZoneTypeId.HasValue && position.ZoneTypeId != line.Material.PreferredZoneTypeId.Value)
        {
            throw new InvalidOperationException(
                $"Location {position.LocationCode} is outside this SKU's required zone and cannot be confirmed. Re-route it to a new location.");
        }

        var otherMaterialAtPosition = await db.StockBatches
            .AsNoTracking()
            .AnyAsync(b => b.PalletPositionId == putaway.PalletPositionId && b.MaterialId != line.MaterialId && b.QuantityBoxes > 0, cancellationToken);
        if (otherMaterialAtPosition)
        {
            throw new InvalidOperationException(
                $"Location {position.LocationCode} now holds a different material and cannot receive this put-away. Re-route it to a new location.");
        }

        var confirmedAtPosition = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.PalletPositionId == putaway.PalletPositionId && b.MaterialId == line.MaterialId)
            .SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
        var effectiveCapacity = PalletCapacityCalculator.EffectiveCapacityBoxes(position, line.Material);
        var free = effectiveCapacity - confirmedAtPosition;
        if (free < putaway.QuantityBoxes)
        {
            throw new InvalidOperationException(
                $"Location {position.LocationCode} no longer has enough free capacity to confirm {putaway.QuantityBoxes:0.###} box(es) " +
                $"(only {Math.Max(0, free):0.###} available now - another confirmation may have used it first). Re-route it to a new location.");
        }

        var batch = await db.StockBatches.FirstOrDefaultAsync(b =>
            b.MaterialId == line.MaterialId &&
            b.PalletPositionId == putaway.PalletPositionId &&
            b.MfgMonth == line.MfgMonth &&
            b.StockSubtypeId == position.LocationSubtypeId,
            cancellationToken);

        if (batch is null)
        {
            db.StockBatches.Add(new StockBatch
            {
                MaterialId = line.MaterialId,
                PalletPositionId = putaway.PalletPositionId,
                MfgMonth = line.MfgMonth,
                StockSubtypeId = position.LocationSubtypeId,
                QuantityBoxes = putaway.QuantityBoxes,
                RowVersion = 1
            });
        }
        else
        {
            batch.QuantityBoxes += putaway.QuantityBoxes;
            batch.RowVersion++;
        }

        putaway.IsConfirmed = true;
        putaway.ConfirmedAt = clock.UtcNow;
        putaway.ConfirmedByUserId = currentUser.UserId;
        putaway.ConfirmedByUserName = currentUser.UserName;
        putaway.RowVersion++;
    }
}
