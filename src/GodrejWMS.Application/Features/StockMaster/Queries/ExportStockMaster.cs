using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster.Queries;

/// <summary>Produces the full Inventory Master as an Excel workbook, in the "Format inventory master" layout.</summary>
public sealed record ExportStockMasterQuery(string? RackCode = null) : IRequest<byte[]>;

public sealed class ExportStockMasterHandler(IApplicationDbContext db, IExcelService excel)
    : IRequestHandler<ExportStockMasterQuery, byte[]>
{
    public async Task<byte[]> Handle(ExportStockMasterQuery request, CancellationToken cancellationToken)
    {
        var query = db.StockBatches
            .AsNoTracking()
            .Where(b => b.QuantityBoxes > 0)
            .Select(b => new
            {
                b.Material.MaterialNumber,
                b.Material.Description,
                b.Material.DesignType,
                MovementTypeCode = b.Material.MovementType.Code,
                MovementTypeName = b.Material.MovementType.DisplayName,
                SeasonCode = b.Material.Season.Code,
                SeasonName = b.Material.Season.DisplayName,
                b.Material.IsActive,
                b.QuantityBoxes,
                b.MfgMonth,
                b.PalletPosition.LocationCode,
                RackCode = b.PalletPosition.Rack.Code
            });

        if (!string.IsNullOrWhiteSpace(request.RackCode))
        {
            query = query.Where(b => b.RackCode == request.RackCode);
        }

        var rows = await query
            .OrderBy(b => b.MaterialNumber).ThenBy(b => b.MfgMonth).ThenBy(b => b.LocationCode)
            .ToListAsync(cancellationToken);

        var dtoRows = rows
            .Select(b => new StockMasterRowDto(
                b.MaterialNumber, b.Description, b.DesignType, b.QuantityBoxes,
                MfgMonthParser.Format(b.MfgMonth), b.LocationCode, b.MovementTypeCode, b.MovementTypeName, b.SeasonCode, b.SeasonName, b.IsActive))
            .ToList();

        return excel.WriteStockMaster(dtoRows);
    }
}
