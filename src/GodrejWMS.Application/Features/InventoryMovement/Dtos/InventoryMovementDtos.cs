namespace GodrejWMS.Application.Features.InventoryMovement.Dtos;

/// <summary>One inventory line ("stock cell") for a selected SKU, across every location it's
/// stored at (Move by SKU's inventory listing). Move by Location's own listing reuses
/// <c>PalletPositionDto.Stock</c> directly instead of this, since it's already scoped to one
/// location; this DTO exists for the cross-location-by-material view that has no existing
/// equivalent.</summary>
public sealed record MaterialStockLineDto(
    int MaterialId,
    long MaterialNumber,
    string MaterialDescription,
    string DesignType,
    int MfgMonth,
    string MfgMonthLabel,
    int PalletPositionId,
    string LocationCode,
    string StockSubtypeName,
    decimal QuantityBoxes);

public sealed record MovementReasonDto(int Id, string Code, string DisplayName, bool IsActive);

/// <summary>One requested line, as supplied by either UI flow - Move by Location always sends a
/// single-element list, Move by SKU sends one element per selected inventory record, but both
/// flow through the exact same validate/confirm commands (§1: "Both options must ultimately use
/// the SAME backend Inventory Movement business logic").</summary>
public sealed record InventoryMovementLineInput(
    int MaterialId,
    int MfgMonth,
    int SourcePalletPositionId,
    int DestinationPalletPositionId,
    decimal QuantityBoxes);

/// <summary>Server-side validation result for one line (§24), checked without persisting
/// anything.</summary>
public sealed record InventoryMovementValidationDto(bool Valid, string? Reason);

/// <summary>Result of one movement line, whether from a single Move by Location or one row of a
/// Move by SKU batch - always independently reported so a multi-line batch can show which lines
/// succeeded and which didn't.</summary>
public sealed record InventoryMovementLineResultDto(
    bool Success,
    string? ErrorMessage,
    string? MovementNumber,
    long MaterialNumber,
    string MfgMonthLabel,
    string SourceLocationCode,
    string DestinationLocationCode,
    decimal QuantityBoxes);

public sealed record InventoryMovementDto(
    int Id,
    string MovementNumber,
    DateTimeOffset CreatedAt,
    long MaterialNumber,
    string MaterialDescription,
    string MfgMonthLabel,
    string SourceLocationCode,
    string DestinationLocationCode,
    decimal QuantityBoxes,
    string ReasonCode,
    string ReasonName,
    string? PerformedByUserName,
    string Status);
