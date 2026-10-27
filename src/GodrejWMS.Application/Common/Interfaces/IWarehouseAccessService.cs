namespace GodrejWMS.Application.Common.Interfaces;

public sealed record WarehouseOption(int Id, string Code, string Name);

/// <summary>Single source of truth for "which warehouses can the current user act on", mirroring
/// the read-side scoping already enforced by AppDbContext's query filters (Superadmin = every
/// active warehouse; everyone else = their directly assigned UserWarehouses, plus every warehouse
/// in any region they're assigned via UserRegions). Used both to populate warehouse pickers in
/// the UI and - the part the UI-only check could never be trusted for - to validate an incoming
/// WarehouseId server-side before a write commits to it.</summary>
public interface IWarehouseAccessService
{
    Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default);
}
