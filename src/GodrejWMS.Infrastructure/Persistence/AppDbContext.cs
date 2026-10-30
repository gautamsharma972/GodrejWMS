using System.Reflection;
using System.Text.Json;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Common;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the whole app: ASP.NET Core Identity tables plus the warehouse domain
/// (materials, racks, pallet positions, stock batches, inward/pullout transactions).
/// </summary>
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentUserService currentUser,
    IDateTimeProvider clock)
    : IdentityDbContext<ApplicationUser>(options), IApplicationDbContext
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<UserWarehouse> UserWarehouses => Set<UserWarehouse>();
    public DbSet<UserRegion> UserRegions => Set<UserRegion>();

    /// <summary>Superadmin and Platform Admin have organization-wide scope. Every other role
    /// (Logistic Manager, Office Staff, Head Supervisor, Supervisor) is scoped - by region or by a
    /// single warehouse, see <see cref="HasWarehouseAccess"/> - per the org-hierarchy access model.</summary>
    private bool HasAllWarehouses => currentUser.IsPlatformLevel();
    private string? AccessUserId => currentUser.UserId;

    // EF Core can translate a plain parameterless property (HasAllWarehouses) inside a query
    // filter, but not a call to a parametrized instance method - so the "does the current user
    // have access to warehouse X" check has to be inlined into every filter below rather than
    // shared via a helper method. Every copy follows this same shape (Superadmin always; a
    // Logistic Manager if X's region is one of their assigned regions; anyone else if X is one of
    // their directly assigned warehouses):
    //   HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == X)
    //       || UserRegions.Any(r => r.UserId == AccessUserId &&
    //           Warehouses.Any(w => w.Id == X && w.City != null && w.City.State.RegionId == r.RegionId))
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<BrandLocationPreference> BrandLocationPreferences => Set<BrandLocationPreference>();
    public DbSet<AopDescription> AopDescriptions => Set<AopDescription>();

    public DbSet<DesignType> DesignTypes => Set<DesignType>();

    public DbSet<SeasonMonthMap> SeasonMonthMaps => Set<SeasonMonthMap>();

    public DbSet<LocationSubtype> LocationSubtypes => Set<LocationSubtype>();
    public DbSet<SkuMovementType> SkuMovementTypes => Set<SkuMovementType>();
    public DbSet<PlantType> PlantTypes => Set<PlantType>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<WarehouseCategory> WarehouseCategories => Set<WarehouseCategory>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<State> States => Set<State>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<ZoneType> ZoneTypes => Set<ZoneType>();
    public DbSet<LocationType> LocationTypes => Set<LocationType>();
    public DbSet<Season> Seasons => Set<Season>();

    public DbSet<Rack> Racks => Set<Rack>();

    public DbSet<PalletPosition> PalletPositions => Set<PalletPosition>();

    public DbSet<StockBatch> StockBatches => Set<StockBatch>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<InwardTransaction> InwardTransactions => Set<InwardTransaction>();

    public DbSet<InwardTransactionLine> InwardTransactionLines => Set<InwardTransactionLine>();

    public DbSet<InwardPutaway> InwardPutaways => Set<InwardPutaway>();

    public DbSet<PulloutTransaction> PulloutTransactions => Set<PulloutTransaction>();

    public DbSet<PulloutTransactionLine> PulloutTransactionLines => Set<PulloutTransactionLine>();

    public DbSet<PulloutPick> PulloutPicks => Set<PulloutPick>();

    public DbSet<MovementReason> MovementReasons => Set<MovementReason>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();

    public DbSet<InventoryAdjustmentLine> InventoryAdjustmentLines => Set<InventoryAdjustmentLine>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Entity<UserWarehouse>().HasOne<ApplicationUser>().WithMany().HasForeignKey(w => w.UserId);
        builder.Entity<Rack>().HasQueryFilter(r => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == r.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == r.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<PalletPosition>().HasQueryFilter(p => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == p.Rack.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == p.Rack.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<BrandLocationPreference>().HasQueryFilter(p => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == p.FromPalletPosition.Rack.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == p.FromPalletPosition.Rack.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<StockBatch>().HasQueryFilter(b => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == b.PalletPosition.Rack.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == b.PalletPosition.Rack.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<InwardTransaction>().HasQueryFilter(t => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == t.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == t.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<InwardTransactionLine>().HasQueryFilter(l => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == l.InwardTransaction.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == l.InwardTransaction.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<InwardPutaway>().HasQueryFilter(p => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == p.InwardTransactionLine.InwardTransaction.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == p.InwardTransactionLine.InwardTransaction.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<PulloutTransaction>().HasQueryFilter(t => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == t.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == t.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<PulloutTransactionLine>().HasQueryFilter(l => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == l.PulloutTransaction.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == l.PulloutTransaction.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<PulloutPick>().HasQueryFilter(p => HasAllWarehouses
            || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == p.PulloutTransactionLine.PulloutTransaction.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == p.PulloutTransactionLine.PulloutTransaction.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<StockMovement>().HasQueryFilter(m =>
            (HasAllWarehouses
                || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == m.SourcePalletPosition.Rack.WarehouseId)
                || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == m.SourcePalletPosition.Rack.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)))
            && (HasAllWarehouses
                || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == m.DestinationPalletPosition.Rack.WarehouseId)
                || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == m.DestinationPalletPosition.Rack.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId))));
        builder.Entity<InventoryAdjustment>().HasQueryFilter(a => HasAllWarehouses
            || UserWarehouses.Any(uw => uw.UserId == AccessUserId && uw.WarehouseId == a.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == a.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        builder.Entity<InventoryAdjustmentLine>().HasQueryFilter(l => HasAllWarehouses
            || UserWarehouses.Any(uw => uw.UserId == AccessUserId && uw.WarehouseId == l.InventoryAdjustment.WarehouseId)
            || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == l.InventoryAdjustment.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)));
        // ActivityLog is a generic audit sink for every entity type in the app, including
        // non-warehouse-scoped master data, so WarehouseId is nullable: rows where it couldn't be
        // resolved (BuildActivityLogs) stay Superadmin-only, the same safe default as before this
        // column existed.
        builder.Entity<ActivityLog>().HasQueryFilter(a => HasAllWarehouses
            || (a.WarehouseId != null && (UserWarehouses.Any(uw => uw.UserId == AccessUserId && uw.WarehouseId == a.WarehouseId)
                || UserRegions.Any(rg => rg.UserId == AccessUserId && Warehouses.Any(w => w.Id == a.WarehouseId && w.City != null && w.City.State.RegionId == rg.RegionId)))));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditEntries = BuildActivityLogs();
        var now = clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                entry.Entity.CreatedByUserId ??= SafeCurrentUserId();
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedByUserId ??= SafeCurrentUserId();
            }
        }

        if (auditEntries.Count > 0)
        {
            ActivityLogs.AddRange(auditEntries);
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private List<ActivityLog> BuildActivityLogs()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is not ActivityLog &&
                        e.Entity is not ApplicationUser &&
                        e.Entity is not Microsoft.AspNetCore.Identity.IdentityRole &&
                        e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var logs = new List<ActivityLog>();
        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => entry.State.ToString()
            };

            var entityName = entry.Metadata.ClrType.Name;
            logs.Add(new ActivityLog
            {
                OccurredAt = clock.UtcNow,
                UserId = SafeCurrentUserId(),
                UserName = SafeCurrentUserName(),
                Action = action,
                EntityName = entityName,
                EntityId = GetPrimaryKey(entry),
                WarehouseId = ResolveWarehouseId(entry.Entity),
                Summary = BuildSummary(action, entityName, entry),
                OldValuesJson = entry.State is EntityState.Added ? null : SerializeValues(entry, originalValues: true),
                NewValuesJson = entry.State is EntityState.Deleted ? null : SerializeValues(entry, originalValues: false)
            });
        }

        return logs;
    }

    /// <summary>Best-effort WarehouseId for the audit row: a direct scalar where the entity has
    /// one, otherwise a navigation chain up to the warehouse-scoped root - which only resolves if
    /// that navigation happens to already be loaded on the tracked entity. Unresolvable or
    /// genuinely warehouse-less entities (shared master data) return null and the row stays
    /// Superadmin-only, per the ActivityLog query filter.</summary>
    private static int? ResolveWarehouseId(object entity) => entity switch
    {
        Rack r => r.WarehouseId,
        InwardTransaction t => t.WarehouseId,
        PulloutTransaction t => t.WarehouseId,
        InventoryAdjustment a => a.WarehouseId,
        PalletPosition p => p.Rack?.WarehouseId,
        StockBatch b => b.PalletPosition?.Rack?.WarehouseId,
        InwardTransactionLine l => l.InwardTransaction?.WarehouseId,
        InwardPutaway p => p.InwardTransactionLine?.InwardTransaction?.WarehouseId,
        PulloutTransactionLine l => l.PulloutTransaction?.WarehouseId,
        PulloutPick p => p.PulloutTransactionLine?.PulloutTransaction?.WarehouseId,
        InventoryAdjustmentLine l => l.InventoryAdjustment?.WarehouseId,
        // Source and destination can differ; the source side is the one the moving stock is
        // attributed to for audit purposes.
        StockMovement m => m.SourcePalletPosition?.Rack?.WarehouseId,
        _ => null
    };

    private string? SafeCurrentUserId()
    {
        try
        {
            return currentUser.UserId;
        }
        catch
        {
            return null;
        }
    }

    private string SafeCurrentUserName()
    {
        try
        {
            return currentUser.UserName ?? "System";
        }
        catch
        {
            return "System";
        }
    }

    private static string? GetPrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
        {
            return null;
        }

        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
            .Where(v => !string.IsNullOrWhiteSpace(v));

        return string.Join(",", values);
    }

    private static string BuildSummary(string action, string entityName, EntityEntry entry)
    {
        var displayValue = GetDisplayValue(entry);
        return string.IsNullOrWhiteSpace(displayValue)
            ? $"{action} {entityName}"
            : $"{action} {entityName} {displayValue}";
    }

    private static string? GetDisplayValue(EntityEntry entry)
    {
        var preferredNames = new[] { "ReferenceNumber", "MaterialNumber", "LocationCode", "Code", "Name", "Description" };
        foreach (var name in preferredNames)
        {
            var property = entry.Properties.FirstOrDefault(p => p.Metadata.Name == name);
            var value = property?.CurrentValue ?? property?.OriginalValue;
            if (value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                return value.ToString();
            }
        }

        return GetPrimaryKey(entry);
    }

    private static string SerializeValues(EntityEntry entry, bool originalValues)
    {
        var values = new SortedDictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsPrimaryKey() || property.Metadata.IsForeignKey() || property.Metadata.IsShadowProperty())
            {
                continue;
            }

            if (entry.State == EntityState.Modified && originalValues && !property.IsModified)
            {
                continue;
            }

            if (entry.State == EntityState.Modified && !originalValues && !property.IsModified)
            {
                continue;
            }

            values[property.Metadata.Name] = originalValues ? property.OriginalValue : property.CurrentValue;
        }

        return JsonSerializer.Serialize(values);
    }
}
