using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryMovement.Queries;

/// <summary>Movement History screen (§29): filterable list of completed Inventory Movements.</summary>
public sealed record GetInventoryMovementHistoryQuery(
    string? Search = null,
    int? MfgMonth = null,
    string? SourceLocationCode = null,
    string? DestinationLocationCode = null,
    string? PerformedByUserName = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int PageNumber = 1,
    int PageSize = 50) : IRequest<PaginatedList<InventoryMovementDto>>;

public sealed class GetInventoryMovementHistoryHandler(IApplicationDbContext db)
    : IRequestHandler<GetInventoryMovementHistoryQuery, PaginatedList<InventoryMovementDto>>
{
    public async Task<PaginatedList<InventoryMovementDto>> Handle(GetInventoryMovementHistoryQuery request, CancellationToken cancellationToken)
    {
        var query = db.StockMovements.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(m =>
                m.MovementNumber.Contains(term) ||
                m.Material.Description.Contains(term) ||
                m.Material.MaterialNumber.ToString().Contains(term));
        }

        if (request.MfgMonth.HasValue)
        {
            query = query.Where(m => m.MfgMonth == request.MfgMonth.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceLocationCode))
        {
            query = query.Where(m => m.SourcePalletPosition.LocationCode == request.SourceLocationCode);
        }

        if (!string.IsNullOrWhiteSpace(request.DestinationLocationCode))
        {
            query = query.Where(m => m.DestinationPalletPosition.LocationCode == request.DestinationLocationCode);
        }

        if (!string.IsNullOrWhiteSpace(request.PerformedByUserName))
        {
            var term = request.PerformedByUserName.Trim();
            query = query.Where(m => m.PerformedByUserName != null && m.PerformedByUserName.Contains(term));
        }

        if (request.FromDate.HasValue)
        {
            var from = request.FromDate.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(m => m.CreatedAt >= from);
        }

        if (request.ToDate.HasValue)
        {
            var to = request.ToDate.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(m => m.CreatedAt <= to);
        }

        var ordered = query.OrderByDescending(m => m.CreatedAt);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var page = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new
            {
                m.Id,
                m.MovementNumber,
                m.CreatedAt,
                m.Material.MaterialNumber,
                MaterialDescription = m.Material.Description,
                m.MfgMonth,
                SourceLocationCode = m.SourcePalletPosition.LocationCode,
                DestinationLocationCode = m.DestinationPalletPosition.LocationCode,
                m.QuantityBoxes,
                ReasonCode = m.Reason.Code,
                ReasonName = m.Reason.DisplayName,
                m.PerformedByUserName,
                m.Status
            })
            .ToListAsync(cancellationToken);

        var items = page
            .Select(m => new InventoryMovementDto(
                m.Id, m.MovementNumber, m.CreatedAt, m.MaterialNumber, m.MaterialDescription,
                MfgMonthParser.Format(m.MfgMonth), m.SourceLocationCode, m.DestinationLocationCode,
                m.QuantityBoxes, m.ReasonCode, m.ReasonName, m.PerformedByUserName, m.Status.ToString()))
            .ToList();

        return new PaginatedList<InventoryMovementDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
