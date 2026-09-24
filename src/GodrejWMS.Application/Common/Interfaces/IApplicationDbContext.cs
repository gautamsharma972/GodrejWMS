using GodrejWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>
/// Persistence seam the Application layer codes against, so it never takes a compile-time
/// dependency on EF Core or MySQL — those live entirely in Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Warehouse> Warehouses { get; }
    DbSet<UserWarehouse> UserWarehouses { get; }
    DbSet<Material> Materials { get; }

    DbSet<DesignType> DesignTypes { get; }

    DbSet<SeasonMonthMap> SeasonMonthMaps { get; }

    DbSet<LocationSubtype> LocationSubtypes { get; }
    DbSet<SkuMovementType> SkuMovementTypes { get; }
    DbSet<ZoneType> ZoneTypes { get; }
    DbSet<LocationType> LocationTypes { get; }
    DbSet<Season> Seasons { get; }

    DbSet<Rack> Racks { get; }

    DbSet<PalletPosition> PalletPositions { get; }

    DbSet<StockBatch> StockBatches { get; }

    DbSet<ActivityLog> ActivityLogs { get; }

    DbSet<InwardTransaction> InwardTransactions { get; }

    DbSet<InwardTransactionLine> InwardTransactionLines { get; }

    DbSet<InwardPutaway> InwardPutaways { get; }

    DbSet<PulloutTransaction> PulloutTransactions { get; }

    DbSet<PulloutTransactionLine> PulloutTransactionLines { get; }

    DbSet<PulloutPick> PulloutPicks { get; }

    DbSet<MovementReason> MovementReasons { get; }

    DbSet<StockMovement> StockMovements { get; }

    ChangeTracker ChangeTracker { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
