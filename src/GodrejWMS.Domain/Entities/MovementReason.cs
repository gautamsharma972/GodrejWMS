using GodrejWMS.Domain.Common;

namespace GodrejWMS.Domain.Entities;

/// <summary>Reason master for Inventory Movement (e.g. "Rack Reorganization", "Space Optimization") - no equivalent master existed in this app, so this is the minimal new reference table the Inventory Movement feature needs.</summary>
public class MovementReason : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class MovementReasonCodes
{
    public const string RackReorganization = "RackReorganization";
    public const string SpaceOptimization = "SpaceOptimization";
    public const string StockConsolidation = "StockConsolidation";
    public const string OperationalRequirement = "OperationalRequirement";
    public const string SupervisorCorrection = "SupervisorCorrection";
}
