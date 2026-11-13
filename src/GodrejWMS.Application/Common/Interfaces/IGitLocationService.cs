using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>The one persistent "set aside as GIT" location per warehouse (GIT-&lt;WH code&gt;) a Picker's Inward put-away
/// routes stock into instead of a real shelf. Unlike <see cref="IVirtualLocationService"/>'s Overflow pool, this is not a
/// recyclable set of slots - a GIT staging area is one physical place, so there is exactly one per warehouse, created on
/// first use and never released.</summary>
public interface IGitLocationService
{
    /// <summary>Returns the warehouse's GIT location, creating it (and its hidden system rack) the first time it is needed.</summary>
    Task<PalletPosition> GetOrCreateAsync(int warehouseId, CancellationToken cancellationToken = default);
}
