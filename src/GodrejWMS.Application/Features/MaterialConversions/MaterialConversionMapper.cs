using GodrejWMS.Application.Common;
using Microsoft.EntityFrameworkCore;
using GodrejWMS.Application.Features.MaterialConversions.Dtos;
using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Features.MaterialConversions;

internal static class MaterialConversionMapper
{
    public static MaterialConversionDto ToDto(MaterialConversion c) => new(
        c.Id, c.ConversionNumber, c.Status,
        c.FromMaterial.MaterialNumber, c.FromMaterial.Description, c.ToMaterial.MaterialNumber, c.ToMaterial.Description,
        c.TotalBoxes, c.Lines.Count, c.ReasonText, c.RequestedByUserName, c.RequestedAt,
        c.ReviewedByUserName, c.ReviewedAt, c.ReviewComment,
        c.Lines.OrderBy(l => l.MfgMonth).ThenBy(l => l.PalletPosition.LocationCode)
            .Select(l => new MaterialConversionLineDto(
                l.Id, l.StockBatchId ?? 0, l.PalletPosition.LocationCode, l.MfgMonth, MfgMonthParser.Format(l.MfgMonth), l.QuantityBoxes, l.SystemQuantityAtRequest))
            .ToList());

    public static IQueryable<MaterialConversion> WithDetail(IQueryable<MaterialConversion> query) => query
        .Include(c => c.FromMaterial).Include(c => c.ToMaterial)
        .Include(c => c.Lines).ThenInclude(l => l.PalletPosition);
}
