using GodrejWMS.Application.Common;
using GodrejWMS.Application.Features.InventoryAdjustments.Dtos;
using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Features.InventoryAdjustments;

internal static class InventoryAdjustmentMapper
{
    public static InventoryAdjustmentDetailDto ToDetail(InventoryAdjustment adjustment)
    {
        var line = adjustment.Lines.Single();
        return new InventoryAdjustmentDetailDto(
            adjustment.Id,
            adjustment.AdjustmentNumber,
            adjustment.RequestedAt,
            adjustment.Status,
            line.Material.MaterialNumber,
            line.Material.Description,
            line.PalletPosition.LocationCode,
            MfgMonthParser.Format(line.MfgMonth),
            line.StockSubtype.DisplayName,
            line.SystemQuantityAtCount,
            line.CountedQuantity,
            line.VarianceQuantity,
            adjustment.ReasonCode,
            adjustment.ReasonText,
            adjustment.RequestedByUserId,
            adjustment.RequestedByUserName,
            adjustment.ReviewedByUserName,
            adjustment.ReviewedAt,
            adjustment.ReviewComment,
            line.ActualQuantityBeforePosting,
            line.ActualQuantityAfterPosting);
    }
}
