using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.InventoryAdjustments.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryAdjustments.Queries;

public sealed record GetInventoryAdjustmentsQuery(
    string? Search = null,
    InventoryAdjustmentStatus? Status = null,
    bool MineOnly = false,
    int PageNumber = 1,
    int PageSize = 25) : IRequest<PaginatedList<InventoryAdjustmentListDto>>;

public sealed class GetInventoryAdjustmentsHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetInventoryAdjustmentsQuery, PaginatedList<InventoryAdjustmentListDto>>
{
    public async Task<PaginatedList<InventoryAdjustmentListDto>> Handle(GetInventoryAdjustmentsQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole("Operator") && !currentUser.IsInRole("Supervisor") && !currentUser.IsInRole("Admin"))
        {
            throw new UnauthorizedAccessException("You are not permitted to view inventory adjustments.");
        }

        var query = db.InventoryAdjustments.AsNoTracking().SelectMany(a => a.Lines.Select(l => new
        {
            Adjustment = a,
            Line = l,
            l.Material.MaterialNumber,
            MaterialDescription = l.Material.Description,
            LocationCode = l.PalletPosition.LocationCode
        }));

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.Adjustment.Status == request.Status.Value);
        }

        if (request.MineOnly || (!currentUser.IsInRole("Supervisor") && !currentUser.IsInRole("Admin")))
        {
            query = query.Where(x => x.Adjustment.RequestedByUserId == currentUser.UserId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x =>
                x.Adjustment.AdjustmentNumber.Contains(term) ||
                x.MaterialNumber.ToString().Contains(term) ||
                x.MaterialDescription.Contains(term) ||
                x.LocationCode.Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Adjustment.RequestedAt)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new
            {
                x.Adjustment.Id,
                x.Adjustment.AdjustmentNumber,
                x.Adjustment.RequestedAt,
                x.Adjustment.Status,
                x.MaterialNumber,
                x.MaterialDescription,
                x.LocationCode,
                x.Line.MfgMonth,
                x.Line.SystemQuantityAtCount,
                x.Line.CountedQuantity,
                x.Line.VarianceQuantity,
                x.Adjustment.ReasonCode,
                x.Adjustment.ReasonText,
                x.Adjustment.RequestedByUserId,
                x.Adjustment.RequestedByUserName,
                x.Adjustment.ReviewedByUserName,
                x.Adjustment.ReviewedAt
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new InventoryAdjustmentListDto(
            x.Id, x.AdjustmentNumber, x.RequestedAt, x.Status, x.MaterialNumber,
            x.MaterialDescription, x.LocationCode, MfgMonthParser.Format(x.MfgMonth),
            x.SystemQuantityAtCount, x.CountedQuantity, x.VarianceQuantity, x.ReasonCode,
            x.ReasonText, x.RequestedByUserId, x.RequestedByUserName, x.ReviewedByUserName, x.ReviewedAt)).ToList();

        return new PaginatedList<InventoryAdjustmentListDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
