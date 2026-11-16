using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>One persistent GIT location per warehouse - see <see cref="IGitLocationService"/>. Looked up by its hidden
/// system rack's code, which `Racks.Code`'s existing global-uniqueness index (see RackConfiguration) also protects
/// against two concurrent first-uses creating a duplicate.</summary>
public sealed class GitLocationService(AppDbContext db) : IGitLocationService
{
    private const int MaxCodeWarehouseLength = 10; // LocationCode is limited to 20: "GIT-" + warehouse code

    public async Task<PalletPosition> GetOrCreateAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        var rackCode = RackCode(warehouseId);
        var existing = await db.PalletPositions
            .Include(p => p.Rack)
            .FirstOrDefaultAsync(p => p.Rack.WarehouseId == warehouseId && p.Rack.Code == rackCode, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var warehouseCode = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == warehouseId)
            .Select(w => w.Code)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The warehouse for this order no longer exists.");
        var codePart = warehouseCode.Length <= MaxCodeWarehouseLength ? warehouseCode : $"W{warehouseId}";

        var rack = new Rack
        {
            WarehouseId = warehouseId,
            Code = rackCode,
            Name = "GIT (picker set-aside)",
            Columns = 0,
            Levels = 1,
            IsActive = true,
            IsSystem = true
        };
        db.Racks.Add(rack);

        var position = new PalletPosition
        {
            Rack = rack,
            Column = 1,
            Level = 1,
            LocationCode = $"GIT-{codePart}",
            LocationTypeId = LocationTypeIds.Floor,
            LocationSubtypeId = LocationSubtypeIds.Good,
            ZoneTypeId = ZoneTypeIds.Git,
            DistancePriority = 9999,
            MaxPallets = 1,
            IsActive = true,
            IsVirtual = true,
            VirtualStatus = VirtualLocationStatus.InUse
        };
        db.PalletPositions.Add(position);
        await db.SaveChangesAsync(cancellationToken);
        return position;
    }

    private static string RackCode(int warehouseId) => $"GIT{warehouseId}";
}
