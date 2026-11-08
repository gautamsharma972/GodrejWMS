using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>Auto-generated overflow locations (VL-&lt;WH&gt;-nnnnnn) for pallets no physical location can take.</summary>
public interface IVirtualLocationService
{
    /// <summary>Hands out one virtual location of the warehouse for a new pallet: the FREE one with the lowest sequence
    /// (locked with SKIP LOCKED so concurrent uploads never get the same one), otherwise a newly created one. It is
    /// marked IN_USE, logged against <paramref name="inwardTransactionId"/> and saved. Must run inside a transaction.</summary>
    Task<PalletPosition> AcquireAsync(int warehouseId, int? inwardTransactionId, CancellationToken cancellationToken = default);

    /// <summary>THE release check, used by every flow after stock leaves a location: a virtual location whose on-hand is zero
    /// and which has no open putaway or pick against it becomes FREE again (and its log row is closed). Returns true when
    /// it was released. Considers changes still pending in the change tracker.</summary>
    Task<bool> ReleaseIfEmptyAsync(PalletPosition position, CancellationToken cancellationToken = default);
}
