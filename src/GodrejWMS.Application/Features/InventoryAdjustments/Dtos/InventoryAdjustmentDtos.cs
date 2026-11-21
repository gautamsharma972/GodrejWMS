using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.InventoryAdjustments.Dtos;

public sealed record InventoryAdjustmentListDto(
    int Id,
    string AdjustmentNumber,
    DateTimeOffset RequestedAt,
    InventoryAdjustmentStatus Status,
    long MaterialNumber,
    string MaterialDescription,
    long? PhysicalMaterialNumber,
    string? PhysicalMaterialDescription,
    string LocationCode,
    string MfgMonthLabel,
    decimal SystemQuantity,
    decimal CountedQuantity,
    decimal VarianceQuantity,
    string ReasonCode,
    string? ReasonText,
    string? RequestedByUserId,
    string? RequestedByUserName,
    string? ReviewedByUserName,
    DateTimeOffset? ReviewedAt,
    /// <summary>The PKM physically found when it differs from the system record's PKM.</summary>
    string? PhysicalMfgMonthLabel = null,
    /// <summary>The stock type physically found when it differs from the system record's.</summary>
    string? PhysicalStockSubtypeName = null);

public sealed record InventoryAdjustmentDetailDto(
    int Id,
    string AdjustmentNumber,
    DateTimeOffset RequestedAt,
    InventoryAdjustmentStatus Status,
    long MaterialNumber,
    string MaterialDescription,
    long? PhysicalMaterialNumber,
    string? PhysicalMaterialDescription,
    string LocationCode,
    string MfgMonthLabel,
    string StockSubtypeName,
    decimal SystemQuantity,
    decimal CountedQuantity,
    decimal VarianceQuantity,
    string ReasonCode,
    string? ReasonText,
    string? RequestedByUserId,
    string? RequestedByUserName,
    string? ReviewedByUserName,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment,
    decimal? ActualQuantityBeforePosting,
    decimal? ActualQuantityAfterPosting,
    decimal? ActualPhysicalQuantityBeforePosting,
    string? PhysicalMfgMonthLabel = null,
    string? PhysicalStockSubtypeName = null);
