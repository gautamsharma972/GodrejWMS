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
        // Same rows as the Current Inventory grid, with the flow status of each (IN-STOCK / GIT-IN / GIT-OUT; GIT-OUT is not in the book stock).
        var rows = await StockFlowRows.LoadAsync(
            db, new GetStockMasterQuery(RackCode: string.IsNullOrWhiteSpace(request.RackCode) ? null : request.RackCode, WithFlowStatus: true), cancellationToken);
        var dtoRows = StockFlowRows.Sort(rows, "material", false).Select(StockFlowRows.ToDto).ToList();

        return excel.WriteStockMaster(dtoRows);
    }
}
