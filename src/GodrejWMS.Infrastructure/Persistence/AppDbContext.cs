using System.Reflection;
using System.Text.Json;
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
    private bool HasAllWarehouses => currentUser.IsInRole("Admin") ||
        currentUser.IsInRole("Supervisor") || currentUser.IsInRole("Operator");
    private string? AccessUserId => currentUser.UserId;
    public DbSet<Material> Materials => Set<Material>();

    public DbSet<DesignType> DesignTypes => Set<DesignType>();

    public DbSet<SeasonMonthMap> SeasonMonthMaps => Set<SeasonMonthMap>();

    public DbSet<LocationSubtype> LocationSubtypes => Set<LocationSubtype>();
    public DbSet<SkuMovementType> SkuMovementTypes => Set<SkuMovementType>();
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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.Entity<UserWarehouse>().HasOne<ApplicationUser>().WithMany().HasForeignKey(w => w.UserId);
        builder.Entity<Rack>().HasQueryFilter(r => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == r.WarehouseId));
        builder.Entity<PalletPosition>().HasQueryFilter(p => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == p.Rack.WarehouseId));
        builder.Entity<StockBatch>().HasQueryFilter(b => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == b.PalletPosition.Rack.WarehouseId));
        builder.Entity<InwardTransaction>().HasQueryFilter(t => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == t.WarehouseId));
        builder.Entity<InwardTransactionLine>().HasQueryFilter(l => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == l.InwardTransaction.WarehouseId));
        builder.Entity<InwardPutaway>().HasQueryFilter(p => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == p.InwardTransactionLine.InwardTransaction.WarehouseId));
        builder.Entity<PulloutTransaction>().HasQueryFilter(t => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == t.WarehouseId));
        builder.Entity<PulloutTransactionLine>().HasQueryFilter(l => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == l.PulloutTransaction.WarehouseId));
        builder.Entity<PulloutPick>().HasQueryFilter(p => HasAllWarehouses || UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == p.PulloutTransactionLine.PulloutTransaction.WarehouseId));
        builder.Entity<StockMovement>().HasQueryFilter(m => HasAllWarehouses ||
            (UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == m.SourcePalletPosition.Rack.WarehouseId) &&
             UserWarehouses.Any(a => a.UserId == AccessUserId && a.WarehouseId == m.DestinationPalletPosition.Rack.WarehouseId)));
        builder.Entity<ActivityLog>().HasQueryFilter(a => HasAllWarehouses);
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
                Summary = BuildSummary(action, entityName, entry),
                OldValuesJson = entry.State is EntityState.Added ? null : SerializeValues(entry, originalValues: true),
                NewValuesJson = entry.State is EntityState.Deleted ? null : SerializeValues(entry, originalValues: false)
            });
        }

        return logs;
    }

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
