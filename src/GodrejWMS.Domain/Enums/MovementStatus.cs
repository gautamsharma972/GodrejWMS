namespace GodrejWMS.Domain.Enums;

/// <summary>Status of a persisted <see cref="Entities.StockMovement"/>. A movement is only ever
/// written once its transaction has fully committed (§17's all-or-nothing rule), so today the only
/// reachable value is <see cref="Completed"/>; the enum exists so a future reversal/cancellation
/// feature doesn't need a schema change.</summary>
public enum MovementStatus
{
    Completed = 0
}
